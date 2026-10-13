using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace LapDock.Services;

public readonly record struct SystemStats(double CpuPercent, double RamPercent, double RamUsedGb, double RamTotalGb, int? BatteryPercent, bool BatteryCharging);

/// <summary>
/// Live CPU / RAM / battery snapshot. Call GetStats() on a 1s timer.
/// CPU is computed from GetSystemTimes deltas — no PerformanceCounter,
/// no extra assemblies, works on a clean Windows install.
/// </summary>
public sealed class SystemStatsService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    private static ulong ToUlong(FILETIME ft) => ((ulong)ft.High << 32) | ft.Low;

    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _primed;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private double GetCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong i = ToUlong(idle), k = ToUlong(kernel), u = ToUlong(user);
        if (!_primed) { _primed = true; _prevIdle = i; _prevKernel = k; _prevUser = u; return 0; }

        // Kernel time includes idle time.
        ulong idleDiff = i - _prevIdle;
        ulong totalDiff = (k - _prevKernel) + (u - _prevUser);
        _prevIdle = i; _prevKernel = k; _prevUser = u;
        if (totalDiff == 0) return 0;
        return Math.Clamp(100.0 - idleDiff * 100.0 / totalDiff, 0, 100);
    }

    public SystemStats GetStats()
    {
        double cpu = GetCpuPercent();

        double ramPct = 0, usedGb = 0, totalGb = 0;
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            ramPct = mem.dwMemoryLoad;
            totalGb = mem.ullTotalPhys / 1073741824.0;
            usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
        }

        int? battery = null;
        bool charging = false;
        try
        {
            var power = Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus != Forms.BatteryChargeStatus.NoSystemBattery &&
                power.BatteryLifePercent is >= 0 and <= 1)
            {
                battery = (int)Math.Round(power.BatteryLifePercent * 100);
                charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            }
        }
        catch { /* desktops have no battery */ }

        return new SystemStats(cpu, ramPct, usedGb, totalGb, battery, charging);
    }
}
