using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LapDock.Services;

/// <summary>
/// Lock, sleep, restart, shut down — the same things the Start menu's power button does.
/// <list type="bullet">
/// <item>Sleep: most current laptops use Modern Standby (S0 low-power idle) and have no classic S3 sleep;
///   there SetSuspendState would hibernate instead, so Sleep turns the display off, which is how the
///   power button sends those machines to standby. Classic S3 machines use SetSuspendState.</item>
/// <item>Restart / shut down: shutdown.exe without /f, so apps with unsaved work can still ask to save.</item>
/// </list>
/// </summary>
public static class PowerService
{
    public static bool Lock() => LockWorkStation();

    /// <param name="window">Any of our windows: the display-off request goes through its default window procedure.</param>
    public static bool Sleep(IntPtr window)
    {
        if (ModernStandby())
        {
            const int WM_SYSCOMMAND = 0x0112, SC_MONITORPOWER = 0xF170, MONITOR_OFF = 2;
            SendMessage(window, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)MONITOR_OFF);
            return true;
        }
        return SetSuspendState(hibernate: false, force: false, disableWakeEvent: false);
    }

    public static bool Restart() => Shutdown("/r /t 0");
    public static bool ShutDown() => Shutdown("/s /t 0");

    private static bool Shutdown(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            return p is not null;
        }
        catch (Exception) { return false; } // blocked by policy
    }

    /// <summary>SYSTEM_POWER_CAPABILITIES.AoAc ("always on, always connected"): byte 20 of the struct.</summary>
    public static bool ModernStandby()
    {
        var caps = Marshal.AllocHGlobal(128); // the struct is 76 bytes; room to spare
        try { return GetPwrCapabilities(caps) && Marshal.ReadByte(caps, 20) != 0; }
        finally { Marshal.FreeHGlobal(caps); }
    }

    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("powrprof.dll")] private static extern bool GetPwrCapabilities(IntPtr caps);
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);
}
