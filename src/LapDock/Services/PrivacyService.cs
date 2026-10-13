using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Windows.Management.Deployment;

namespace LapDock.Services;

/// <summary>Apps currently using the microphone / camera (display names, never invented).</summary>
public sealed record PrivacyState(IReadOnlyList<string> Microphone, IReadOnlyList<string> Camera)
{
    public static readonly PrivacyState None = new(Array.Empty<string>(), Array.Empty<string>());
    public bool MicrophoneInUse => Microphone.Count > 0;
    public bool CameraInUse => Camera.Count > 0;
}

/// <summary>
/// Microphone / camera "in use" detection from Windows' capability consent store — the same
/// per-app data behind Settings → Privacy → "Recent activity". Each app has a key with
/// LastUsedTimeStart/LastUsedTimeStop; Stop == 0 means it is using the device right now.
/// A background thread waits on RegNotifyChangeKeyValue, so there is no polling.
/// </summary>
public sealed class PrivacyService : IDisposable
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    private readonly Dispatcher _dispatcher;
    private readonly ManualResetEvent _stop = new(false);
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private Thread? _thread;

    public PrivacyState Current { get; private set; } = PrivacyState.None;

    /// <summary>Raised on the UI thread with (previous, current).</summary>
    public event Action<PrivacyState, PrivacyState>? Changed;

    public PrivacyService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start()
    {
        Current = Scan();
        _thread = new Thread(Watch) { IsBackground = true, Name = "LapDock privacy watcher" };
        _thread.Start();
    }

    private void Watch()
    {
        try
        {
            using var mic = Registry.CurrentUser.OpenSubKey(Store + "microphone");
            using var cam = Registry.CurrentUser.OpenSubKey(Store + "webcam");
            using var micChanged = new AutoResetEvent(false);
            using var camChanged = new AutoResetEvent(false);
            var waits = new WaitHandle[] { _stop, micChanged, camChanged };
            while (true)
            {
                // One-shot notifications: re-armed every round, covering every app's subkey.
                Arm(mic, micChanged);
                Arm(cam, camChanged);
                if (WaitHandle.WaitAny(waits) == 0) return;
                if (_stop.WaitOne(150)) return; // an app writes Start/Stop in bursts — coalesce them
                var state = Scan();
                _dispatcher.BeginInvoke(() => Apply(state));
            }
        }
        catch (Exception)
        {
            // Registry unavailable (locked-down machine): indicators simply never light up.
        }
    }

    private static void Arm(RegistryKey? key, AutoResetEvent signal)
    {
        if (key is null) return;
        const uint REG_NOTIFY_CHANGE_NAME = 0x1, REG_NOTIFY_CHANGE_LAST_SET = 0x4;
        RegNotifyChangeKeyValue(key.Handle, true, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET,
            signal.SafeWaitHandle, true);
    }

    private void Apply(PrivacyState state)
    {
        var previous = Current;
        if (state.Microphone.SequenceEqual(previous.Microphone) && state.Camera.SequenceEqual(previous.Camera)) return;
        Current = state;
        Changed?.Invoke(previous, state);
    }

    private PrivacyState Scan() => new(InUse("microphone"), InUse("webcam"));

    private List<string> InUse(string capability)
    {
        var apps = new List<string>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Store + capability);
            if (root is null) return apps;
            foreach (var name in root.GetSubKeyNames())
            {
                if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    using var desktop = root.OpenSubKey(name);
                    foreach (var exeKey in desktop?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var k = desktop!.OpenSubKey(exeKey);
                        var exe = exeKey.Replace('#', '\\');
                        // A crashed app can leave Stop = 0 behind; only trust it while the exe is running.
                        if (IsActive(k) && IsRunning(exe)) apps.Add(DesktopName(exe));
                    }
                }
                else
                {
                    using var k = root.OpenSubKey(name);
                    if (IsActive(k)) apps.Add(PackageName(name));
                }
            }
        }
        catch (Exception) { /* partial results are fine */ }
        return apps.Distinct().OrderBy(a => a).ToList();
    }

    private static bool IsActive(RegistryKey? k) =>
        k?.GetValue("LastUsedTimeStart") is long start && start != 0 &&
        k.GetValue("LastUsedTimeStop") is long stop && stop == 0;

    private static bool IsRunning(string exePath)
    {
        var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath));
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    /// <summary>"C:\…\Discord.exe" → "Discord" (the exe's own product description).</summary>
    private string DesktopName(string exePath) => _names.GetOrAdd(exePath, path =>
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (Exception) { }
        return Path.GetFileNameWithoutExtension(path);
    });

    /// <summary>"MSTeams_8wekyb3d8bbwe" → "Microsoft Teams" (the package's display name).</summary>
    private string PackageName(string familyName) => _names.GetOrAdd(familyName, family =>
    {
        try
        {
            var package = new PackageManager().FindPackagesForUser(string.Empty, family).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(package?.DisplayName)) return package.DisplayName;
        }
        catch (Exception) { }
        var id = family.Split('_')[0];      // "Microsoft.WindowsCamera"
        return id[(id.LastIndexOf('.') + 1)..]; // "WindowsCamera" — still the real identity, just unprettified
    });

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(1000);
        _stop.Dispose();
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle hKey, bool watchSubtree, uint notifyFilter,
        SafeWaitHandle hEvent, bool asynchronous);
}
