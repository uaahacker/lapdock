using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LapDock.Models;
using LapDock.Services;
using Forms = System.Windows.Forms;

namespace LapDock.Views;

public enum HotkeyKind { Settings, Clipboard }

/// <summary>
/// LapDock's settings: a floating card centred on the notch's display, summoned by the settings
/// hotkey, the tray icon or the notch's right-click menu. Esc, clicking away or × hides it.
/// Every change is saved at once and handed back to the notch through <c>applied</c>.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly Duration Anim = TimeSpan.FromMilliseconds(150);
    private readonly AppSettings _settings;
    private readonly Action _applied;
    private readonly Func<HotkeyKind, string, bool> _setHotkey;
    private bool _open, _exiting;

    /// <param name="applied">Settings changed: re-place / refresh the notch.</param>
    /// <param name="setHotkey">Tries a new settings hotkey; false = invalid or taken (the old one stays).</param>
    public SettingsWindow(AppSettings settings, Action applied, Func<HotkeyKind, string, bool> setHotkey)
    {
        InitializeComponent();
        _settings = settings;
        _applied = applied;
        _setHotkey = setHotkey;
        DataContext = settings;
        Deactivated += (_, _) => Collapse();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Collapse(); };
        HotkeyBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHotkey(HotkeyKind.Settings); };
        CityBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await ApplyCityAsync(); };
        ClipHotkeyBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHotkey(HotkeyKind.Clipboard); };
    }

    public void Toggle(Forms.Screen screen)
    {
        if (_open) Collapse();
        else Summon(screen);
    }

    public void Summon(Forms.Screen screen)
    {
        if (_open) { Activate(); return; }
        _open = true;
        HotkeyBox.Text = _settings.SummonHotkey;
        ClipHotkeyBox.Text = _settings.ClipboardHotkey;
        CityBox.Text = _settings.CityName;
        AutostartBox.IsChecked = InstallService.StartWithWindows;
        bool installed = InstallService.IsInstalled;
        AboutText.Text = installed ? $"Version {InstallService.Version}" : $"Version {InstallService.Version} · running without installing";
        UninstallButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        BuildDisplays();
        // Never taller than the screen it opens on (it scrolls instead).
        MaxHeight = screen.WorkingArea.Height / (VisualTreeHelper.GetDpi(this).DpiScaleY is > 0 and var k ? k : 1) - 40;
        Show(); // still at Opacity 0: placing after Show lets a DPI change between displays rescale it first
        ScreenService.PlaceCenter(this, screen);
        Activate();
        Animate(1, 1);
    }

    public void Collapse()
    {
        if (!_open) return;
        _open = false;
        var fade = Animate(0, 0.97);
        fade.Completed += (_, _) => { if (!_open) Hide(); };
    }

    private DoubleAnimation Animate(double opacity, double scale)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(opacity, Anim) { EasingFunction = ease };
        BeginAnimation(OpacityProperty, fade);
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, Anim) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, Anim) { EasingFunction = ease });
        return fade;
    }

    /// <summary>One pill per attached display; the notch's display is selected.</summary>
    private void BuildDisplays()
    {
        Displays.Children.Clear();
        var current = ScreenService.FindByDeviceName(_settings.ScreenDeviceName)?.DeviceName;
        int n = 0;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            n++;
            var label = $"Display {n} · {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? " · main" : "")}";
            var pill = new Button { Content = label, Style = (Style)FindResource("OptionPill"), Tag = screen.DeviceName == current ? "on" : null };
            pill.Click += (_, _) =>
            {
                _settings.ScreenDeviceName = screen.DeviceName;
                Save();
                BuildDisplays();
            };
            Displays.Children.Add(pill);
        }
    }

    private void ApplyHotkey_Click(object sender, RoutedEventArgs e) => ApplyHotkey(HotkeyKind.Settings);
    private void ApplyClipHotkey_Click(object sender, RoutedEventArgs e) => ApplyHotkey(HotkeyKind.Clipboard);

    private void ApplyHotkey(HotkeyKind kind)
    {
        bool clipboard = kind == HotkeyKind.Clipboard;
        var (box, status, current) = clipboard
            ? (ClipHotkeyBox, ClipHotkeyStatus, _settings.ClipboardHotkey)
            : (HotkeyBox, HotkeyStatus, _settings.SummonHotkey);
        var gesture = box.Text.Trim();
        if (gesture == current) return;
        if (_setHotkey(kind, gesture))
        {
            status.Text = $"Saved. {gesture} now opens {(clipboard ? "clipboard history" : "Settings")}.";
            status.Foreground = (Brush)FindResource("AccentBrush");
            Save();
        }
        else
        {
            status.Text = $"\"{gesture}\" isn't a valid shortcut, or another app already uses it. Kept {current}.";
            status.Foreground = (Brush)FindResource("TextBrush");
        }
    }

    private void Changed_Click(object sender, RoutedEventArgs e) => Save(); // the binding has already updated the setting

    private async void ApplyCity_Click(object sender, RoutedEventArgs e) => await ApplyCityAsync();

    private async Task ApplyCityAsync()
    {
        var name = CityBox.Text.Trim();
        if (name.Length == 0) return;
        CityStatus.Text = "Looking it up…";
        if (await WeatherService.FindPlaceAsync(name) is not { } place)
        {
            CityStatus.Text = $"Couldn't find \"{name}\". Check the spelling or try the nearest larger city.";
            return;
        }
        _settings.CityName = place.Name;
        _settings.Latitude = place.Latitude;
        _settings.Longitude = place.Longitude;
        CityBox.Text = place.Name;
        CityStatus.Text = string.IsNullOrEmpty(place.Country) ? $"Saved. Today shows the weather in {place.Name}." : $"Saved. Today shows the weather in {place.Name}, {place.Country}.";
        Save();
    }

    private void Autostart_Click(object sender, RoutedEventArgs e) => InstallService.StartWithWindows = AutostartBox.IsChecked == true;

    /// <summary>Same as Settings → Apps → LapDock → Uninstall.</summary>
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        Collapse();
        try { Process.Start(new ProcessStartInfo(InstallService.InstalledExe, "--uninstall") { UseShellExecute = false }); }
        catch (Exception) { /* the installed copy is missing: nothing to uninstall */ }
    }

    private void Save()
    {
        SettingsService.Save(_settings);
        _applied();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Collapse();

    /// <summary>Alt+F4 only hides it; the app closes it for real on quit.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting) { e.Cancel = true; Collapse(); }
        base.OnClosing(e);
    }

    public void Exit()
    {
        _exiting = true;
        Close();
    }
}
