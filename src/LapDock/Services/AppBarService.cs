using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LapDock.Services;

/// <summary>
/// "Make room": registers the notch as an appbar (the taskbar's own mechanism) that reserves a thin strip
/// along the top of its display. Windows shrinks that display's work area, so maximised and snapped apps
/// (Chrome's tab strip, title-bar buttons) start below the notch instead of under it. An appbar always
/// reserves the full width of its edge; the strip beside the pill simply shows the desktop.
/// </summary>
public sealed class AppBarService : IDisposable
{
    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABE_TOP = 1;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly uint _callbackMessage = RegisterWindowMessage("LapDock.AppBar");
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated"); // Explorer (re)started
    private readonly List<DateTime> _changes = new();
    private (System.Drawing.Rectangle Screen, int Height)? _reserved;

    /// <summary>
    /// The circuit breaker tripped: the strip changed too often, so it was given back for good.
    /// Every change makes Windows resize all maximised apps; a storm of them freezes the desktop.
    /// </summary>
    public event Action? Tripped;
    public bool IsTripped { get; private set; }

    public AppBarService(Window owner)
    {
        _hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(Hook);
    }

    public bool IsReserved => _reserved is not null;

    /// <param name="screen">The notch's display, physical pixels.</param>
    /// <param name="height">Strip height in physical pixels (the collapsed pill's height).</param>
    public void Reserve(System.Drawing.Rectangle screen, int height)
    {
        if (IsTripped || (_reserved is { } r && r.Screen == screen && r.Height == height)) return;
        if (TooManyChanges(_changes, DateTime.UtcNow))
        {
            Release();
            IsTripped = true;
            Tripped?.Invoke();
            return;
        }
        var data = Data();
        if (_reserved is null) SHAppBarMessage(ABM_NEW, ref data);
        data.uEdge = ABE_TOP;
        data.rc = new RECT { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Top + height };
        SHAppBarMessage(ABM_QUERYPOS, ref data); // the system may move it (e.g. below a taskbar docked at the top)
        data.rc.Bottom = data.rc.Top + height;
        SHAppBarMessage(ABM_SETPOS, ref data);   // reserves it: maximised windows reflow below
        _reserved = (screen, height);
    }

    /// <summary>Gives the strip back: the work area returns to normal.</summary>
    public void Release()
    {
        if (_reserved is null) return;
        var data = Data();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _reserved = null;
    }

    /// <summary>
    /// Appbar notifications are deliberately ignored. Reserving the strip makes Windows announce "appbar
    /// positions changed" to every appbar, this one included; answering by reserving again looped forever
    /// and starved the notch's UI thread. LapDock reserves at start, on display changes and when the setting changes.
    /// </summary>
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The one exception: when Explorer restarts it forgets every appbar. Claim the strip again, once
        // (and it counts towards the breaker).
        if (msg == _taskbarCreated && _reserved is { } r)
        {
            _reserved = null;
            Reserve(r.Screen, r.Height);
        }
        return IntPtr.Zero;
    }

    /// <summary>Records a change; true when there were more than 5 in the last 30 seconds.</summary>
    public static bool TooManyChanges(List<DateTime> changes, DateTime now)
    {
        changes.RemoveAll(t => now - t > TimeSpan.FromSeconds(30));
        changes.Add(now);
        return changes.Count > 5;
    }

    /// <summary>Debug-build self-check of the breaker (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        var log = new List<DateTime>();
        var t0 = new DateTime(2026, 10, 13, 22, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++) Debug.Assert(!TooManyChanges(log, t0.AddSeconds(i)));   // start, display changes: fine
        Debug.Assert(TooManyChanges(log, t0.AddSeconds(6)));                                   // a sixth within 30 s: stop
        var calm = new List<DateTime>();
        for (int i = 0; i < 20; i++) Debug.Assert(!TooManyChanges(calm, t0.AddSeconds(i * 10))); // one every 10 s: never trips
    }

    private APPBARDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _callbackMessage,
    };

    public void Dispose()
    {
        Release();
        _source.RemoveHook(Hook);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
}
