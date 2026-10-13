using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace LapDock.Services;

/// <summary>
/// Quick-settings state for the Controls tab. Only real toggles are offered:
/// <list type="bullet">
/// <item>Wi-Fi / Bluetooth — WinRT <see cref="Radio"/> (the same switches as Windows' quick settings).</item>
/// <item>Airplane — every radio off / back on. Windows' own airplane flag has no public API,
///   so this is the radios-off equivalent, not the Settings switch itself.</item>
/// <item>Dark mode — the two Personalize registry values + the ImmersiveColorSet broadcast.</item>
/// <item>Night light / Do not disturb — NO public toggle API: state is read where possible and
///   the tile opens the Settings page instead of pretending to switch.</item>
/// </list>
/// Event-driven: radio, network and theme changes push <see cref="Changed"/>; nothing polls.
/// </summary>
public sealed class TogglesService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string NightLightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\"
        + @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private readonly Dispatcher _dispatcher;
    private Radio? _wifi, _bluetooth;

    /// <summary>UI thread.</summary>
    public event Action? Changed;

    public TogglesService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool HasWifi => _wifi is not null;
    public bool HasBluetooth => _bluetooth is not null;
    public bool WifiOn => _wifi?.State == RadioState.On;
    public bool BluetoothOn => _bluetooth?.State == RadioState.On;
    public bool AirplaneOn => (HasWifi || HasBluetooth) && !WifiOn && !BluetoothOn;
    /// <summary>Connected Wi-Fi network name, or null.</summary>
    public string? WifiName { get; private set; }

    public bool DarkMode =>
        Registry.CurrentUser.OpenSubKey(PersonalizeKey)?.GetValue("AppsUseLightTheme") is int light && light == 0;

    /// <summary>
    /// Night light on/off, or null when unknown. The state lives in an undocumented CloudStore blob.
    /// ponytail: byte-18 heuristic (0x15 = on, 0x13 = off) used by common tools; if Windows changes the
    /// blob layout this returns null and the tile just says "Opens Settings".
    /// </summary>
    public bool? NightLightOn =>
        Registry.CurrentUser.OpenSubKey(NightLightKey)?.GetValue("Data") is byte[] { Length: > 18 } data
            ? data[18] switch { 0x15 => true, 0x13 => false, _ => null }
            : null;

    public async Task StartAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
            {
                var radios = await Radio.GetRadiosAsync();
                _wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                _bluetooth = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                foreach (var radio in new[] { _wifi, _bluetooth })
                    if (radio is not null) radio.StateChanged += OnRadioChanged;
            }
        }
        catch (Exception) { /* no radio stack: those tiles open Settings instead */ }

        NetworkInformation.NetworkStatusChanged += OnNetworkChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        OnNetworkChanged(null);
    }

    /// <summary>false = Windows refused (policy, airplane mode set in Settings…) or there is no such radio.</summary>
    public Task<bool> SetWifiAsync(bool on) => SetAsync(_wifi, on);
    public Task<bool> SetBluetoothAsync(bool on) => SetAsync(_bluetooth, on);

    public async Task<bool> SetAirplaneAsync(bool on)
    {
        bool wifi = await SetAsync(_wifi, !on);
        bool bluetooth = await SetAsync(_bluetooth, !on);
        return wifi || bluetooth;
    }

    private static async Task<bool> SetAsync(Radio? radio, bool on)
    {
        if (radio is null) return false;
        try { return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed; }
        catch (Exception) { return false; }
    }

    /// <summary>Flips apps + system (taskbar, Start) between light and dark, like the Settings switch.</summary>
    public async Task SetDarkModeAsync(bool dark)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        }
        // Broadcasting waits on every top-level window: keep it off the UI thread.
        await Task.Run(() => SendMessageTimeout((IntPtr)0xFFFF /* HWND_BROADCAST */, 0x001A /* WM_SETTINGCHANGE */,
            IntPtr.Zero, "ImmersiveColorSet", 0x0002 /* SMTO_ABORTIFHUNG */, 200, out _));
        Raise();
    }

    // Radio / network events arrive on worker threads.
    private void OnRadioChanged(Radio sender, object args) => Raise();

    private void OnNetworkChanged(object? sender)
    {
        WifiName = ConnectedWifi();
        Raise();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color) Raise();
    }

    private void Raise() => _dispatcher.BeginInvoke(() => Changed?.Invoke());

    private static string? ConnectedWifi()
    {
        try
        {
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (!profile.IsWlanConnectionProfile || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) continue;
                // Windows 11 24H2+ hides the SSID without location permission; the profile name is the SSID anyway.
                var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
                return string.IsNullOrEmpty(ssid) ? profile.ProfileName : ssid;
            }
        }
        catch (Exception) { }
        return null;
    }

    public void Dispose()
    {
        NetworkInformation.NetworkStatusChanged -= OnNetworkChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; // static events: must unhook
        foreach (var radio in new[] { _wifi, _bluetooth })
            if (radio is not null) radio.StateChanged -= OnRadioChanged;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint flags, uint timeoutMs, out IntPtr result);
}
