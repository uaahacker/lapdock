using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace LapDock.Services;

/// <summary>Display enumeration + "put this window exactly on that display".</summary>
public static class ScreenService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// Saved display if it is still attached; otherwise the first NON-primary
    /// display (with an external monitor as main, that is the laptop panel).
    /// </summary>
    public static Forms.Screen? FindByDeviceName(string? deviceName) =>
        Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName)
        ?? Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary)
        ?? Forms.Screen.PrimaryScreen;

    public static Forms.Screen ScreenOf(Window window) => Forms.Screen.FromHandle(Handle(window));

    /// <summary>
    /// Pins the window to the top-centre of <paramref name="screen"/>, flush with its top edge.
    /// SetWindowPos works in PHYSICAL pixels: WPF Left/Top are DIPs, which land wrong on the
    /// 125%/150%-scaled panels laptops ship with.
    /// </summary>
    public static void PlaceTopCenter(Window window, Forms.Screen screen)
    {
        var h = Handle(window);
        var b = screen.Bounds;
        // Land on the target display first: if its scaling differs, WM_DPICHANGED rescales
        // the window right now, so its real physical width is known before centring.
        SetWindowPos(h, IntPtr.Zero, b.Left, b.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        GetWindowRect(h, out var r);
        SetWindowPos(h, IntPtr.Zero, b.Left + (b.Width - (r.Right - r.Left)) / 2, b.Top, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
