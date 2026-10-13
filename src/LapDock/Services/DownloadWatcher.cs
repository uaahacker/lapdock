using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LapDock.Services;

/// <summary>
/// Browser downloads into the Downloads folder. While downloading, Chrome and Edge write "name.crdownload",
/// Firefox "name.part"; the browser renames it to the real name when it's done. The total size is only
/// known inside the browser, so progress is the bytes received so far.
/// </summary>
public sealed class DownloadWatcher : IDisposable
{
    private static readonly string[] PartialExtensions = { ".crdownload", ".part", ".partial" };

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, DateTime> _active = new(StringComparer.OrdinalIgnoreCase); // partial file → started
    private FileSystemWatcher? _watcher;

    /// <summary>The download to show: the oldest one running for more than a second (quick ones just finish).</summary>
    public (string Name, long Bytes, int Count)? Current { get; private set; }

    /// <summary>UI thread: a download started, grew or ended.</summary>
    public event Action? Changed;
    /// <summary>UI thread: the finished file.</summary>
    public event Action<string>? Completed;

    public DownloadWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _tick.Tick += (_, _) => Update();
    }

    public void Start()
    {
        var folder = DownloadsFolder();
        if (!Directory.Exists(folder)) return;
        _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
        _watcher.Created += (_, e) => _dispatcher.BeginInvoke(() => Seen(e.FullPath));
        _watcher.Renamed += (_, e) => _dispatcher.BeginInvoke(() => Renamed(e.OldFullPath, e.FullPath));
        _watcher.Deleted += (_, e) => _dispatcher.BeginInvoke(() => { if (_active.Remove(e.FullPath)) Update(); });
        _watcher.EnableRaisingEvents = true;
    }

    public static bool IsPartial(string path) =>
        PartialExtensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>"report.pdf.crdownload" → "report.pdf".</summary>
    public static string FinalName(string path)
    {
        var name = Path.GetFileName(path);
        return IsPartial(name) ? Path.GetFileNameWithoutExtension(name) : name;
    }

    private void Seen(string path)
    {
        if (!IsPartial(path) || _active.ContainsKey(path)) return;
        _active[path] = DateTime.UtcNow;
        _tick.Start();
        Update();
    }

    private void Renamed(string from, string to)
    {
        if (!_active.Remove(from, out var started))
        {
            Seen(to);
            return;
        }
        if (IsPartial(to)) _active[to] = started; // Chrome: "Unconfirmed 123.crdownload" → "name.crdownload"
        else Completed?.Invoke(to);              // done: renamed to its real name
        Update();
    }

    private void Update()
    {
        foreach (var gone in _active.Keys.Where(p => !File.Exists(p)).ToList()) _active.Remove(gone); // cancelled
        var shown = _active.Where(a => DateTime.UtcNow - a.Value > TimeSpan.FromSeconds(1)).OrderBy(a => a.Value).ToList();
        var next = shown.Count == 0 ? ((string, long, int)?)null : (FinalName(shown[0].Key), Size(shown[0].Key), _active.Count);
        _tick.IsEnabled = _active.Count > 0;
        if (next == Current) return;
        Current = next;
        Changed?.Invoke();
    }

    private static long Size(string path)
    {
        try { return new FileInfo(path).Length; } catch (Exception) { return 0; }
    }

    /// <summary>"12.4 MB"</summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void ShowInFolder(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch (Exception) { }
    }

    private static string DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads (it can be moved)
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path) == 0) return path;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out string path);

    public void Dispose()
    {
        _tick.Stop();
        _watcher?.Dispose();
    }

    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        Debug.Assert(FinalName(@"C:\D\report.pdf.crdownload") == "report.pdf");
        Debug.Assert(FinalName(@"C:\D\setup.exe.part") == "setup.exe");
        Debug.Assert(FinalName(@"C:\D\photo.jpg") == "photo.jpg");
        Debug.Assert(!IsPartial(@"C:\D\notes.txt") && IsPartial(@"C:\D\x.CRDOWNLOAD"));
        Debug.Assert(FormatBytes(500) == "1 KB" && FormatBytes(5 * 1024 * 1024) == "5 MB" && FormatBytes(3L * 1024 * 1024 * 1024) == "3 GB");
    }
}
