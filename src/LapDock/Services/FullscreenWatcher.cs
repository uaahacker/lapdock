using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LapDock.Services;

/// <summary>
/// Is an app fullscreen on the notch's display right now — a video, a game, a slideshow? Switching apps
/// is reported by Windows at once. A browser going fullscreen in place only resizes its own window, so the
/// front window is also checked once a second — two trivial calls. (Subscribing to every move/resize instead
/// woke LapDock for each caret and scroll in the front app: measurably more CPU than this.)
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;

    // Shell surfaces that open over a fullscreen app for a moment (Start, search, Alt+Tab, tray, menus):
    // while one of them has the focus, keep the current state instead of flickering.
    private static readonly HashSet<string> Transient = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame", "ForegroundStaging", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow", "#32768",
    };
    // The desktop itself covers the screen but isn't an app.
    private static readonly HashSet<string> Desktop = new(StringComparer.Ordinal) { "Progman", "WorkerW" };

    private readonly Window _owner;
    private readonly WinEventDelegate _callback; // a field: Windows keeps calling it, the GC must not collect it
    private readonly IntPtr _foregroundHook;
    private readonly DispatcherTimer _glance = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _disposed;

    public bool IsFullscreen { get; private set; }

    /// <summary>An app window lies under the notch (its tab strip or title bar runs beneath the pill).</summary>
    public bool IsTopCovered { get; private set; }

    /// <summary>UI thread (out-of-context hooks are delivered to the thread that set them).</summary>
    public event Action? Changed;

    public FullscreenWatcher(Window owner)
    {
        _owner = owner;
        _callback = OnWinEvent;
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        _glance.Tick += (_, _) => Check();
        _glance.Start();
        Check();
    }

    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Events already queued can still arrive after Dispose (closing the notch itself changes the foreground).
        if (_disposed) return;
        try { Check(); }
        catch (Exception) { /* called from Windows: never let an exception escape into native code */ }
    }

    /// <summary>Re-evaluates now (also after display changes).</summary>
    public void Check()
    {
        var own = new WindowInteropHelper(_owner).Handle;
        if (_disposed || own == IntPtr.Zero) return; // the notch is gone (quitting)
        var screen = ToRect(ScreenService.ScreenOf(_owner).Bounds);
        bool full = IsFullscreen;
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out int pid);
        var cls = foreground == IntPtr.Zero ? "" : ClassName(foreground);
        // Fullscreen is about the app in front. While the notch, Settings, the tray menu or a passing shell
        // surface (Start, Alt+Tab…) has the focus, keep what we decided before.
        if (foreground != IntPtr.Zero && pid != Environment.ProcessId && !Transient.Contains(cls))
            full = !Desktop.Contains(cls) && GetWindowRect(foreground, out var window) && Covers(window, screen, IsZoomed(foreground));
        // What lies under the notch doesn't depend on the focus at all.
        bool covered = !full && SomethingUnder(own, screen);
        if (full == IsFullscreen && covered == IsTopCovered) return;
        IsFullscreen = full;
        IsTopCovered = covered;
        Changed?.Invoke();
    }

    /// <summary>
    /// Is an app window right under the notch? Walks the windows below it, front to back, and probes the
    /// top edge at the pill's middle and both ends. Skips what isn't really there: hidden, minimised,
    /// cloaked (suspended store apps), click-through overlays, our own windows and the desktop.
    /// </summary>
    private static bool SomethingUnder(IntPtr own, RECT screen)
    {
        if (!GetWindowRect(own, out var notch)) return false;
        int mid = (notch.Left + notch.Right) / 2, y = screen.Top + 4;
        var probes = new[] { mid - 90, mid, mid + 90 };
        for (var w = GetWindow(own, GW_HWNDNEXT); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
        {
            if (!IsWindowVisible(w) || IsIconic(w)) continue;
            if (DwmGetWindowAttribute(w, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if ((GetWindowLong(w, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0) continue;
            GetWindowThreadProcessId(w, out int pid);
            if (pid == Environment.ProcessId || !GetWindowRect(w, out var r)) continue;
            if (!probes.Any(x => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)) continue;
            return !Desktop.Contains(ClassName(w)); // the first real window there decides
        }
        return false;
    }

    /// <summary>
    /// Fullscreen on a display = the window covers all of it. A maximised window can cover it too (with an
    /// auto-hiding taskbar its frame overhangs every edge), so a maximised window only counts on an exact fit.
    /// </summary>
    public static bool Covers(RECT window, RECT screen, bool maximised)
    {
        bool covers = window.Left <= screen.Left && window.Top <= screen.Top && window.Right >= screen.Right && window.Bottom >= screen.Bottom;
        bool exact = window.Left == screen.Left && window.Top == screen.Top && window.Right == screen.Right && window.Bottom == screen.Bottom;
        return covers && (!maximised || exact);
    }

    private static RECT ToRect(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public void Dispose()
    {
        _disposed = true;
        _glance.Stop();
        UnhookWinEvent(_foregroundHook);
    }

    /// <summary>Debug-build self-check of the fullscreen rule (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        var screen = new RECT(0, 0, 1920, 1080);
        Debug.Assert(Covers(new RECT(0, 0, 1920, 1080), screen, maximised: false));        // fullscreen video / borderless game
        Debug.Assert(Covers(new RECT(0, 0, 1920, 1080), screen, maximised: true));         // fullscreen entered from a maximised window
        Debug.Assert(Covers(new RECT(-1, -1, 1921, 1081), screen, maximised: false));      // exclusive fullscreen overhanging a pixel
        Debug.Assert(!Covers(new RECT(-8, -8, 1928, 1088), screen, maximised: true));      // maximised, auto-hide taskbar: not fullscreen
        Debug.Assert(!Covers(new RECT(-8, -8, 1928, 1040), screen, maximised: true));      // maximised above the taskbar
        Debug.Assert(!Covers(new RECT(100, 100, 900, 700), screen, maximised: false));     // ordinary window
        Debug.Assert(!Covers(new RECT(1920, 0, 3840, 1080), screen, maximised: false));    // fullscreen on the other display
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct RECT(int Left, int Top, int Right, int Bottom);

    private delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    private const uint GW_HWNDNEXT = 2;
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, DWMWA_CLOAKED = 14;
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
}
