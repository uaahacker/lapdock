using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LapDock.Services;

/// <summary>
/// Screenshots are Windows' job; LapDock only starts them:
///  • Rectangle / Window → Snipping Tool's capture overlay in that mode (ms-screenclip: URI).
///    Snipping Tool then shows its usual notification, editor and auto-save.
///  • Full screen → Win+PrintScreen, which Windows saves straight to Pictures\Screenshots.
/// "Screenshot saved" comes from watching the Screenshots known folder — LapDock never
/// touches the clipboard or the image itself.
/// </summary>
public sealed class ScreenshotService : IDisposable
{
    private static readonly Guid ScreenshotsFolderId = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    private readonly Dispatcher _dispatcher;
    private FileSystemWatcher? _watcher;
    private DateTime _lastEvent;

    /// <summary>UI thread; full path of the new screenshot file.</summary>
    public event Action<string>? Saved;

    public ScreenshotService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start() => TryWatch();

    public void Rectangle() => Snip("Rectangle");
    public void Window() => Snip("Window");

    /// <summary>Opens the Snipping Tool app itself (recording, delay, settings…).</summary>
    public static void OpenApp() => Shell(@"shell:AppsFolder\Microsoft.ScreenSketch_8wekyb3d8bbwe!App");

    /// <summary>Full-screen capture of every display, saved by Windows to Pictures\Screenshots.</summary>
    public void FullScreen()
    {
        TryWatch();
        const byte VK_LWIN = 0x5B, VK_SNAPSHOT = 0x2C;
        const int KEYUP = 0x2;
        keybd_event(VK_LWIN, 0, 0, 0);
        keybd_event(VK_SNAPSHOT, 0, 0, 0);
        keybd_event(VK_SNAPSHOT, 0, KEYUP, 0);
        keybd_event(VK_LWIN, 0, KEYUP, 0);
    }

    private void Snip(string mode)
    {
        TryWatch();
        try
        {
            // The legacy ms-screenclip: parameters are deprecated but still honoured by Snipping Tool.
            // Its replacement (ms-screenclip://capture) only works for packaged (MSIX) apps.
            Shell($"ms-screenclip:?clippingMode={mode}");
        }
        catch (Exception)
        {
            // No Snipping Tool URI handler: Win+Shift+S opens the same overlay.
            const byte VK_LWIN = 0x5B, VK_SHIFT = 0x10, VK_S = 0x53;
            const int KEYUP = 0x2;
            keybd_event(VK_LWIN, 0, 0, 0); keybd_event(VK_SHIFT, 0, 0, 0);
            keybd_event(VK_S, 0, 0, 0); keybd_event(VK_S, 0, KEYUP, 0);
            keybd_event(VK_SHIFT, 0, KEYUP, 0); keybd_event(VK_LWIN, 0, KEYUP, 0);
        }
    }

    private static void Shell(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    /// <summary>The Screenshots folder only exists after the first screenshot, so retry lazily.</summary>
    private void TryWatch()
    {
        if (_watcher is not null) return;
        try
        {
            if (SHGetKnownFolderPath(ScreenshotsFolderId, 0, IntPtr.Zero, out var path) != 0 || !Directory.Exists(path)) return;
            _watcher = new FileSystemWatcher(path) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
            _watcher.Created += (_, e) =>
            {
                if (!WallpaperService.IsImage(e.FullPath)) return;
                _dispatcher.BeginInvoke(() =>
                {
                    // One capture can create more than one file; report it once.
                    if (DateTime.UtcNow - _lastEvent < TimeSpan.FromSeconds(2)) return;
                    _lastEvent = DateTime.UtcNow;
                    Saved?.Invoke(e.FullPath);
                });
            };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception) { _watcher = null; }
    }

    public void Dispose() => _watcher?.Dispose();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, int flags, int extraInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token,
        [MarshalAs(UnmanagedType.LPWStr)] out string path);
}
