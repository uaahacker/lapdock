using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;

namespace LapDock.Services;

public sealed record AppMemory(string Key, string Name, ImageSource? Icon, long Bytes);

/// <summary>
/// "Top apps by memory", counted like Task Manager: private working set, summed per app
/// (Chrome's twenty processes are one row). Names and icons come from each app's exe, cached.
/// </summary>
public sealed class ProcessMemoryService : IDisposable
{
    // Kernel/system entries Task Manager doesn't list as apps.
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
        { "_Total", "Idle", "System", "Registry", "Memory Compression", "Secure System" };

    private readonly PdhCounter? _workingSet = PdhCounter.TryOpen(@"\Process(*)\Working Set - Private");
    private readonly Dictionary<string, (string Name, ImageSource? Icon)> _identity = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _order = new();

    public bool Available => _workingSet is not null;

    public List<AppMemory> Top(int count)
    {
        if (_workingSet is null) return new();
        var bytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in _workingSet.Read())
        {
            var key = instance.Split('#')[0]; // "chrome#3" → "chrome"
            if (Hidden.Contains(key)) continue;
            bytes[key] = bytes.GetValueOrDefault(key) + (long)value;
        }
        _order = StableOrder(_order, bytes, count);
        return _order.Select(key =>
        {
            var (name, icon) = Identify(key);
            return new AppMemory(key, name, icon, bytes[key]);
        }).ToList();
    }

    /// <summary>
    /// Top <paramref name="count"/> by size — but a list that reshuffles every refresh is unreadable, so
    /// the same apps keep their previous order unless one now beats an app above it by more than 5%.
    /// </summary>
    public static List<string> StableOrder(IReadOnlyList<string> previous, IReadOnlyDictionary<string, long> bytes, int count)
    {
        var fresh = bytes.OrderByDescending(kv => kv.Value).Take(count).Select(kv => kv.Key).ToList();
        if (previous.Count != fresh.Count || fresh.Except(previous).Any()) return fresh;
        for (int i = 0; i < previous.Count; i++)
            for (int j = i + 1; j < previous.Count; j++)
                if (bytes[previous[j]] > bytes[previous[i]] * 1.05) return fresh;
        return previous.ToList();
    }

    private (string Name, ImageSource? Icon) Identify(string key)
    {
        if (_identity.TryGetValue(key, out var known)) return known;
        string name = key;
        ImageSource? icon = null;
        if (ExePath(key) is { } path)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description)) name = description;
            }
            catch (Exception) { }
            icon = ShellIconService.Get(path, 64).Icon;
        }
        return _identity[key] = (name, icon);
    }

    /// <summary>The exe behind a process name. PROCESS_QUERY_LIMITED_INFORMATION works on most processes, even elevated ones.</summary>
    private static string? ExePath(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p)
            {
                var handle = OpenProcess(0x1000, false, p.Id);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(handle, 0, sb, ref size) && File.Exists(sb.ToString())) return sb.ToString();
                }
                finally { CloseHandle(handle); }
            }
        }
        return null;
    }

    public void Dispose() => _workingSet?.Dispose();

    /// <summary>Debug-build self-check of the ordering rule (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        var bytes = new Dictionary<string, long> { ["a"] = 100, ["b"] = 103, ["c"] = 50 };
        Debug.Assert(StableOrder(new[] { "a", "b", "c" }, bytes, 3).SequenceEqual(new[] { "a", "b", "c" })); // within 5%: stays put
        bytes["b"] = 110;
        Debug.Assert(StableOrder(new[] { "a", "b", "c" }, bytes, 3).SequenceEqual(new[] { "b", "a", "c" })); // clearly bigger: re-sort
        Debug.Assert(StableOrder(Array.Empty<string>(), bytes, 2).SequenceEqual(new[] { "b", "a" }));        // first run
        bytes["d"] = 500;
        Debug.Assert(StableOrder(new[] { "b", "a" }, bytes, 2).SequenceEqual(new[] { "d", "b" }));            // new app enters
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
