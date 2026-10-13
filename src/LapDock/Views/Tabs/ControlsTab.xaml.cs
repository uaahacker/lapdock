using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using LapDock.Controls;
using System.Windows.Media;
using LapDock.Services;

namespace LapDock.Views.Tabs;

/// <summary>
/// Quick settings: Wi-Fi, Bluetooth, Audio, Night light, Dark mode, Do not disturb, Airplane,
/// plus master volume. A tile that can't really toggle opens the matching Settings page instead.
/// </summary>
public partial class ControlsTab : UserControl, IDisposable
{
    private sealed record Tile(Button Button, Border Circle, FrameworkElement Glyph, TextBlock Subtitle);

    private readonly TogglesService _toggles;
    private readonly Tile _wifi, _bluetooth, _audioTile, _micTile, _nightLight, _dark, _dnd, _airplane;
    private readonly AudioService _mic;
    private AudioService? _audio;
    private bool _started, _updatingVolume;

    public ControlsTab()
    {
        InitializeComponent();
        _toggles = new TogglesService(Dispatcher);
        _toggles.Changed += Refresh;
        _mic = new AudioService(Dispatcher, capture: true);
        _mic.Changed += _ => Refresh();

        _wifi = AddTile("wifi", "Wi-Fi", async () =>
        {
            if (!await _toggles.SetWifiAsync(!_toggles.WifiOn)) MainWindow.OpenUri("ms-settings:network-wifi");
        });
        _bluetooth = AddTile("bluetooth", "Bluetooth", async () =>
        {
            if (!await _toggles.SetBluetoothAsync(!_toggles.BluetoothOn)) MainWindow.OpenUri("ms-settings:bluetooth");
        });
        _audioTile = AddTile("volume", "Audio", () =>
        {
            if (_audio is { Available: true }) _audio.ToggleMute();
            else MainWindow.OpenUri("ms-settings:sound");
            return Task.CompletedTask;
        });
        _micTile = AddTile("\uE720", "Microphone", () =>
        {
            if (_mic.Available) _mic.ToggleMute();
            else MainWindow.OpenUri("ms-settings:sound");
            return Task.CompletedTask;
        });
        // No public API toggles night light or DND: these two open Settings (see TogglesService).
        _nightLight = AddTile("sun", "Night light", () => Open("ms-settings:nightlight"));
        _dark = AddTile("moon", "Dark mode", () => _toggles.SetDarkModeAsync(!_toggles.DarkMode));
        _dnd = AddTile("", "Do not disturb", () =>
            Open(Environment.OSVersion.Version.Build >= 22000 ? "ms-settings:notifications" : "ms-settings:quiethours"));
        _airplane = AddTile("plane", "Airplane", async () =>
        {
            if (!await _toggles.SetAirplaneAsync(!_toggles.AirplaneOn)) MainWindow.OpenUri("ms-settings:network-airplanemode");
        });

        // The notch refreshes this tab each time it opens (night light has no change event).
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            _mic.Start();
            await _toggles.StartAsync();
        };
    }

    /// <summary>Shares the notch's services (one Core Audio / SMTC subscription for the whole app).</summary>
    public void Attach(AudioService? audio, NowPlayingService? player)
    {
        if (player is not null) Music.Attach(player);
        else Music.Visibility = Visibility.Collapsed; // music switched off in Settings
        _audio = audio;
        if (_audio is not null) _audio.Changed += OnVolumeChanged;
        Refresh();
    }

    private static Task Open(string uri)
    {
        MainWindow.OpenUri(uri);
        return Task.CompletedTask;
    }

    /// <param name="icon">A LapDock icon name, or a Segoe glyph for what the pack doesn't draw (microphone, DND).</param>
    private Tile AddTile(string icon, string title, Func<Task> click)
    {
        FrameworkElement glyphText = LineIcon.Exists(icon)
            ? new LineIcon { Icon = icon, Width = 21, Height = 21 }
            : new TextBlock { Text = icon, Style = (Style)FindResource("Icon"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center };
        var circle = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Child = glyphText };
        var subtitle = new TextBlock { FontSize = 12, Foreground = (Brush)FindResource("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
        text.Children.Add(subtitle);
        var body = new DockPanel();
        body.Children.Add(circle);
        body.Children.Add(text);

        var button = new Button { Style = (Style)FindResource("QuickTile"), Content = body };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false; // radios take a moment: no double toggles
            try { await click(); }
            catch (Exception) { /* the tile keeps showing the real state */ }
            finally { button.IsEnabled = true; Refresh(); }
        };
        TileGrid.Children.Add(button);
        return new Tile(button, circle, glyphText, subtitle);
    }

    private void Set(Tile tile, bool on, string subtitle)
    {
        tile.Circle.Background = (Brush)FindResource(on ? "AccentBrush" : "PanelFillBrush");
        tile.Glyph.SetValue(TextElement.ForegroundProperty, on ? Brushes.Black : (Brush)FindResource("TextBrush"));
        tile.Subtitle.Text = subtitle;
        tile.Button.ToolTip = subtitle;
    }

    private static string OnOff(bool on) => on ? "On" : "Off";

    public void Refresh()
    {
        var t = _toggles;
        Set(_wifi, t.WifiOn, !t.HasWifi ? "Opens Settings" : !t.WifiOn ? "Off" : t.WifiName ?? "Not connected");
        Set(_bluetooth, t.BluetoothOn, t.HasBluetooth ? OnOff(t.BluetoothOn) : "Opens Settings");
        Set(_airplane, t.AirplaneOn, t.HasWifi || t.HasBluetooth ? OnOff(t.AirplaneOn) : "Opens Settings");
        Set(_dark, t.DarkMode, OnOff(t.DarkMode));
        var night = t.NightLightOn;
        Set(_nightLight, night == true, night is { } n ? $"{OnOff(n)} · opens Settings" : "Opens Settings");
        Set(_dnd, false, "Opens Settings");

        bool audio = _audio is { Available: true };
        Set(_audioTile, audio && !_audio!.Muted, !audio ? "No output device" : _audio!.Muted ? "Muted" : _audio.DeviceName ?? "Speakers");
        Set(_micTile, _mic.Available && !_mic.Muted, !_mic.Available ? "No microphone" : _mic.Muted ? "Muted" : _mic.DeviceName ?? "On");
        UpdateVolume();
    }

    private void OnVolumeChanged(bool external) => Refresh();

    private void UpdateVolume()
    {
        bool available = _audio is { Available: true };
        VolumeSlider.IsEnabled = MuteButton.IsEnabled = available;
        if (!available) { VolumeValue.Text = ""; return; }
        int percent = (int)Math.Round(_audio!.Volume * 100);
        _updatingVolume = true;
        VolumeSlider.Value = percent;
        _updatingVolume = false;
        VolumeValue.Text = _audio.Muted ? "Muted" : $"{percent}%";
        MuteIcon.Opacity = _audio.Muted ? 0.45 : 1;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingVolume) _audio?.SetVolume((float)(e.NewValue / 100));
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => _audio?.ToggleMute();

    public void Dispose()
    {
        _toggles.Dispose();
        _mic.Dispose();
        if (_audio is not null) _audio.Changed -= OnVolumeChanged;
    }
}
