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

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    public static IReadOnlyList<Forms.Screen> AllScreens => Forms.Screen.AllScreens;

    public static Forms.Screen? FindByDeviceName(string? deviceName) =>
        Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName)
        ?? Forms.Screen.PrimaryScreen;

    /// <summary>
    /// Covers the chosen display exactly. Uses SetWindowPos with PHYSICAL pixels:
    /// WPF Left/Top are DIPs, which land wrong on the 125%/150%-scaled panels
    /// laptops ship with — and exact placement is the whole point of LapDock.
    /// </summary>
    public static void MoveToScreen(Window window, Forms.Screen screen)
    {
        var area = screen.WorkingArea;
        window.WindowState = WindowState.Normal;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd, IntPtr.Zero, area.Left, area.Top, area.Width, area.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        // Now that the window sits on the target monitor, maximising snaps to it.
        window.WindowState = WindowState.Maximized;
    }

    public static string FriendlyName(Forms.Screen s, int index) =>
        $"Screen {index + 1}{(s.Primary ? " (main)" : "")} — {s.Bounds.Width}×{s.Bounds.Height}";
}
