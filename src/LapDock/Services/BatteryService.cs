using System.Windows.Threading;
using Microsoft.Win32;
using Windows.System.Power;
using Forms = System.Windows.Forms;

namespace LapDock.Services;

public enum BatteryState { Normal, Low, Critical, Full }

public readonly record struct BatteryInfo(bool Present, int Percent, bool Charging, bool PluggedIn, BatteryState State, TimeSpan? TimeLeft);

/// <summary>
/// Live battery state, event-driven: WinRT PowerManager raises an event on every
/// percent / charging / power-supply change, so nothing is polled. If PowerManager is
/// unavailable it falls back to WinForms PowerStatus + SystemEvents.PowerModeChanged.
/// Desktops without a battery report Present = false and the notch hides the widget.
/// </summary>
public sealed class BatteryService : IDisposable
{
    // Windows' own Battery Saver threshold is 20%; 10% is the usual "critical" warning level.
    public const int LowPercent = 20, CriticalPercent = 10;

    private readonly Dispatcher _dispatcher;
    private bool _usingPowerManager;

    public BatteryInfo Current { get; private set; }

    /// <summary>Raised on the UI thread with (previous, current).</summary>
    public event Action<BatteryInfo, BatteryInfo>? Changed;

    public BatteryService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start()
    {
        try
        {
            PowerManager.RemainingChargePercentChanged += OnPowerChanged;
            PowerManager.BatteryStatusChanged += OnPowerChanged;
            PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
            _usingPowerManager = true;
        }
        catch (Exception)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        Current = Read();
    }

    private void OnPowerChanged(object? sender, object e) => _dispatcher.BeginInvoke(Refresh);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange) _dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        var previous = Current;
        Current = Read();
        if (Current != previous) Changed?.Invoke(previous, Current);
    }

    private BatteryInfo Read()
    {
        try
        {
            if (_usingPowerManager)
            {
                if (PowerManager.BatteryStatus == BatteryStatus.NotPresent) return default;
                int percent = PowerManager.RemainingChargePercent;
                bool plugged = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
                bool charging = PowerManager.BatteryStatus == BatteryStatus.Charging;
                TimeSpan? left = plugged ? null : PowerManager.RemainingDischargeTime;
                return Make(percent, charging, plugged, left);
            }

            var power = Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery) ||
                power.BatteryChargeStatus == Forms.BatteryChargeStatus.Unknown)
                return default;
            bool online = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            return Make((int)Math.Round(power.BatteryLifePercent * 100),
                power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.Charging), online,
                online || power.BatteryLifeRemaining < 0 ? null : TimeSpan.FromSeconds(power.BatteryLifeRemaining));
        }
        catch (Exception)
        {
            return default; // a broken battery driver must never take the notch down
        }
    }

    private static BatteryInfo Make(int percent, bool charging, bool plugged, TimeSpan? left)
    {
        percent = Math.Clamp(percent, 0, 100);
        // Plugged in and no longer charging near the top = full. (A charge limit such as
        // 80% "conservation mode" stays Normal and simply reads "Plugged in".)
        var state = plugged && !charging && percent >= 95 ? BatteryState.Full
            : !plugged && percent <= CriticalPercent ? BatteryState.Critical
            : !plugged && percent <= LowPercent ? BatteryState.Low
            : BatteryState.Normal;
        // Windows reports "unknown" remaining time as a huge value right after unplugging.
        if (left is { } t && (t <= TimeSpan.Zero || t > TimeSpan.FromDays(2))) left = null;
        return new BatteryInfo(true, percent, charging, plugged, state, left);
    }

    public void Dispose()
    {
        if (_usingPowerManager)
        {
            PowerManager.RemainingChargePercentChanged -= OnPowerChanged;
            PowerManager.BatteryStatusChanged -= OnPowerChanged;
            PowerManager.PowerSupplyStatusChanged -= OnPowerChanged;
        }
        else SystemEvents.PowerModeChanged -= OnPowerModeChanged; // static event: must unhook
    }
}
