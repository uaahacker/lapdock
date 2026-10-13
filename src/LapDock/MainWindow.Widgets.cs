using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LapDock.Services;
using Microsoft.Win32;

namespace LapDock;

/// <summary>
/// System widgets: clock, battery, privacy (mic/camera), now playing, volume, screenshot,
/// wallpaper — plus the short-lived "activity" messages they raise. Every widget is optional
/// (AppSettings flags) and starts in isolation, so one failing never takes the notch down.
/// </summary>
public partial class MainWindow
{
    private readonly NowPlayingService _nowPlaying = new();
    private BatteryService? _battery;
    private PrivacyService? _privacy;
    private AudioService? _audio;
    private ScreenshotService? _screenshots;

    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _eqTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private readonly DispatcherTimer _trackTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private string? _panel;            // open detail panel: snip | volume | wallpaper | battery | privacy
    private string? _lastTrack;        // "title|artist" last announced
    private string? _wallpaperPath;
    private (string Text, DateTime At) _lastNotice;
    private bool _seeking, _updatingVolume, _mediaReady;
    private int _eqTick;

    private Brush Res(string key) => (Brush)FindResource(key);

    private async Task StartWidgetsAsync()
    {
        _activityTimer.Tick += (_, _) => { _activityTimer.Stop(); if (_mode == Mode.Activity) SetMode(Mode.Peek); };
        _eqTimer.Tick += (_, _) => AnimateEq();
        _trackTimer.Tick += (_, _) => { _trackTimer.Stop(); AnnounceTrack(); };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();
        BuildWallpaperStyles();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (_settings.ShowBattery)
            Try(() => { _battery = new BatteryService(Dispatcher); _battery.Changed += OnBatteryChanged; _battery.Start(); });
        if (_settings.MicrophoneIndicator || _settings.CameraIndicator)
            Try(() => { _privacy = new PrivacyService(Dispatcher); _privacy.Changed += OnPrivacyChanged; _privacy.Start(); });
        Try(() => { _audio = new AudioService(Dispatcher); _audio.Changed += OnVolumeChanged; _audio.Start(); });
        Try(() => { _screenshots = new ScreenshotService(Dispatcher); _screenshots.Saved += OnScreenshotSaved; _screenshots.Start(); });

        if (_settings.MediaControls)
        {
            MediaCard.DataContext = _nowPlaying;
            _nowPlaying.PropertyChanged += OnNowPlayingChanged;
            await _nowPlaying.InitializeAsync(); // handles its own failures (SMTC unavailable → no media section)
            _lastTrack = TrackKey(); // don't announce what was already playing at startup
        }
        _mediaReady = true;
        UpdateMediaControls();
        UpdateVolumeUi();
        RefreshAll();
    }

    private static void Try(Action start)
    {
        try { start(); } catch (Exception) { /* this widget stays hidden; the rest keep working */ }
    }

    private void StopWidgets()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; // static event: must unhook
        foreach (var t in new[] { _clockTimer, _eqTimer, _activityTimer, _trackTimer }) t.Stop();
        _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
        _nowPlaying.Dispose();
        _battery?.Dispose();
        _privacy?.Dispose();
        _audio?.Dispose();
        _screenshots?.Dispose();
    }

    private void RefreshAll()
    {
        RefreshPeek();
        RefreshExpanded();
    }

    // ------------------------------------------------------------------ peek
    private void RefreshPeek()
    {
        bool media = _settings.MediaControls && _nowPlaying.IsPlaying;
        PeekArtBrush.ImageSource = _nowPlaying.Artwork;
        PeekArt.Visibility = Show(media && _nowPlaying.Artwork is not null);
        PeekTitle.Text = _nowPlaying.Title;
        PeekTitle.Visibility = Show(media);
        PeekEq.Visibility = Show(media);
        _eqTimer.IsEnabled = media; // the only animation that runs on its own, and only while music plays
        PeekTime.Visibility = Show(!media && _settings.ShowClock);

        var p = _privacy?.Current ?? PrivacyState.None;
        PeekMicDot.Visibility = Show(_settings.MicrophoneIndicator && p.MicrophoneInUse);
        PeekCamDot.Visibility = Show(_settings.CameraIndicator && p.CameraInUse);

        var b = _battery?.Current ?? default;
        PeekBattery.Visibility = Show(b.Present);
        PeekBatteryText.Text = $"{b.Percent}%";
        PeekBatteryText.Foreground = b.State is BatteryState.Low or BatteryState.Critical ? BatteryBrush(b) : Res("TextBrush");
        PeekBatteryGlyph.Text = BatteryGlyph(b);
        PeekBatteryGlyph.Foreground = BatteryBrush(b);

        if (_mode == Mode.Peek) ResizePill();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        PeekTime.Text = now.ToString("t");
        StatusClock.Text = $"{now:dddd, d MMMM}  ·  {now:t}"; // ("t" inside a custom format would mean just "P" of PM)
        StatusClock.Visibility = Show(_settings.ShowClock);
        // Wake once a minute, right after it turns over — not every second.
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second + 0.05);
        if (_mode == Mode.Peek) ResizePill();
    }

    private void AnimateEq()
    {
        _eqTick++;
        var bars = new[] { Eq1, Eq2, Eq3, Eq4 };
        for (int i = 0; i < bars.Length; i++)
            bars[i].Height = 3 + 9 * Math.Abs(Math.Sin(_eqTick * .45 + i * .9));
    }

    // -------------------------------------------------------------- expanded
    private void RefreshExpanded()
    {
        var p = _privacy?.Current ?? PrivacyState.None;
        bool mic = _settings.MicrophoneIndicator && p.MicrophoneInUse;
        bool cam = _settings.CameraIndicator && p.CameraInUse;
        PrivacyChip.Visibility = Show(mic || cam);
        ChipMic.Visibility = Show(mic);
        ChipCam.Visibility = Show(cam);

        var b = _battery?.Current ?? default;
        BatteryChip.Visibility = Show(b.Present);
        ChipBatteryText.Text = $"{b.Percent}%";
        ChipBatteryGlyph.Text = BatteryGlyph(b);
        ChipBatteryGlyph.Foreground = BatteryBrush(b);

        MediaCard.Visibility = Show(_settings.MediaControls && _nowPlaying.HasSession);
        VolumeTool.Visibility = Show(_audio?.Available == true);

        if (_panel is not null) RefreshPanel(_panel);
        if (_mode == Mode.Expanded) ResizePill();
    }

    private void ShowPanel(string? name)
    {
        _panel = name;
        SnipPanel.Visibility = Show(name == "snip");
        VolumePanel.Visibility = Show(name == "volume");
        WallpaperPanel.Visibility = Show(name == "wallpaper");
        BatteryPanel.Visibility = Show(name == "battery");
        PrivacyPanel.Visibility = Show(name == "privacy");
        PanelHost.Visibility = Show(name is not null);
        SnipTool.Tag = name == "snip" ? "on" : null;
        VolumeTool.Tag = name == "volume" ? "on" : null;
        WallpaperTool.Tag = name == "wallpaper" ? "on" : null;
        if (name is not null) RefreshPanel(name);
        if (_mode == Mode.Expanded) ResizePill();
    }

    private void TogglePanel(string name) => ShowPanel(_panel == name ? null : name);

    private void RefreshPanel(string name)
    {
        switch (name)
        {
            case "volume": UpdateVolumeUi(); break;
            case "wallpaper": _ = RefreshWallpaperPanelAsync(); break;
            case "battery": UpdateBatteryPanel(); break;
            case "privacy": UpdatePrivacyPanel(); break;
        }
    }

    private void SnipTool_Click(object sender, RoutedEventArgs e) => TogglePanel("snip");
    private void VolumeTool_Click(object sender, RoutedEventArgs e) => TogglePanel("volume");
    private void WallpaperTool_Click(object sender, RoutedEventArgs e) => TogglePanel("wallpaper");
    private void BatteryChip_Click(object sender, RoutedEventArgs e) => TogglePanel("battery");
    private void PrivacyChip_Click(object sender, RoutedEventArgs e) => TogglePanel("privacy");

    private static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception) { /* nothing registered for it — nothing to open */ }
    }

    // ------------------------------------------------------------- activity
    /// <summary>
    /// A few seconds of "Charging 54%" in the collapsed notch. Skipped while expanded (the same
    /// information is on screen). Repeats of the same key update in place instead of re-animating.
    /// </summary>
    private void Notify(string key, string glyph, string title, string detail, double? progress = null,
        Brush? accent = null, ImageSource? art = null)
    {
        if (!_settings.DynamicNotifications || _mode == Mode.Expanded) return;
        var text = title + "|" + detail;
        if (_mode != Mode.Activity && _lastNotice.Text == text && DateTime.UtcNow - _lastNotice.At < TimeSpan.FromSeconds(5)) return;
        _lastNotice = (text, DateTime.UtcNow);

        ActivityGlyph.Text = glyph;
        ActivityGlyph.Foreground = accent ?? Res("TextBrush");
        ActivityGlyph.Visibility = Show(art is null);
        ActivityIcon.Background = art is null ? Res("FillBrush") : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        ActivityTitle.Text = title;
        ActivityDetail.Text = detail;
        ActivityDetail.Visibility = Show(!string.IsNullOrEmpty(detail));
        ActivityBar.Visibility = Show(progress is not null);
        ActivityBar.Value = progress ?? 0;
        ActivityBar.Foreground = accent ?? Res("TextBrush");

        _activityTimer.Stop();
        _activityTimer.Start();
        if (_mode == Mode.Activity) ResizePill();
        else SetMode(Mode.Activity);
    }

    // --------------------------------------------------------------- battery
    private void OnBatteryChanged(BatteryInfo old, BatteryInfo now)
    {
        RefreshAll();
        if (!old.Present || !now.Present) return; // first reading, or no battery: nothing to announce
        double level = now.Percent / 100.0;
        if (now.PluggedIn != old.PluggedIn)
        {
            if (now.PluggedIn)
                Notify("battery", BatteryGlyph(now), now.State == BatteryState.Full ? "Fully charged" : now.Charging ? "Charging" : "Plugged in",
                    $"{now.Percent}%", level, Res("GreenBrush"));
            else
                Notify("battery", BatteryGlyph(now), "On battery", $"{now.Percent}%{TimeLeft(now, " · ")}", level, BatteryBrush(now));
        }
        else if (now.State != old.State)
        {
            switch (now.State)
            {
                case BatteryState.Low:
                    Notify("battery", BatteryGlyph(now), "Low battery", $"{now.Percent}% · plug in soon", level, Res("OrangeBrush")); break;
                case BatteryState.Critical:
                    Notify("battery", BatteryGlyph(now), "Battery critically low", $"{now.Percent}% · plug in now", level, Res("RedBrush")); break;
                case BatteryState.Full:
                    Notify("battery", BatteryGlyph(now), "Fully charged", "100% — you can unplug", level, Res("GreenBrush")); break;
            }
        }
    }

    private void UpdateBatteryPanel()
    {
        var b = _battery?.Current ?? default;
        BatteryBig.Text = $"{b.Percent}%";
        BatteryStatus.Text = BatteryStatusText(b);
        BatteryBar.Value = b.Percent;
        BatteryBar.Foreground = b.State is BatteryState.Low or BatteryState.Critical ? BatteryBrush(b) : Res("GreenBrush");
    }

    /// <summary>Segoe Fluent battery glyphs: Battery0–10 at U+EBA0, BatteryCharging0–10 at U+EBAB.</summary>
    private static string BatteryGlyph(BatteryInfo b)
    {
        int level = Math.Clamp((int)Math.Round(b.Percent / 10.0), 0, 10);
        return ((char)((b.Charging ? 0xEBAB : 0xEBA0) + level)).ToString();
    }

    private Brush BatteryBrush(BatteryInfo b) => b.State switch
    {
        BatteryState.Critical => Res("RedBrush"),
        BatteryState.Low => Res("OrangeBrush"),
        _ => b.Charging ? Res("GreenBrush") : Res("TextBrush"),
    };

    private static string BatteryStatusText(BatteryInfo b)
    {
        if (!b.Present) return "No battery";
        if (b.State == BatteryState.Full) return "Fully charged";
        if (b.Charging) return "Charging";
        if (b.PluggedIn) return "Plugged in, not charging";
        var prefix = b.State switch { BatteryState.Critical => "Critically low", BatteryState.Low => "Low battery", _ => "On battery" };
        return prefix + TimeLeft(b, " · ");
    }

    private static string TimeLeft(BatteryInfo b, string separator) =>
        b.TimeLeft is { } t ? $"{separator}{(t.TotalHours >= 1 ? $"{(int)t.TotalHours} h " : "")}{t.Minutes} min left" : "";

    private void BatterySettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:batterysaver");

    // --------------------------------------------------------------- privacy
    private void OnPrivacyChanged(PrivacyState old, PrivacyState now)
    {
        RefreshAll();
        if (_settings.MicrophoneIndicator && now.Microphone.Except(old.Microphone).Any())
            Notify("mic", "", "Microphone in use", string.Join(", ", now.Microphone), accent: Res("OrangeBrush"));
        else if (_settings.CameraIndicator && now.Camera.Except(old.Camera).Any())
            Notify("camera", "", "Camera in use", string.Join(", ", now.Camera), accent: Res("GreenBrush"));
    }

    private void UpdatePrivacyPanel()
    {
        var p = _privacy?.Current ?? PrivacyState.None;
        PrivacyMicRow.Visibility = Show(_settings.MicrophoneIndicator);
        PrivacyMicText.Text = p.MicrophoneInUse ? "In use by " + string.Join(", ", p.Microphone) : "Not in use";
        PrivacyMicGlyph.Foreground = p.MicrophoneInUse ? Res("OrangeBrush") : Res("MutedBrush");
        PrivacyCamRow.Visibility = Show(_settings.CameraIndicator);
        PrivacyCamText.Text = p.CameraInUse ? "In use by " + string.Join(", ", p.Camera) : "Not in use";
        PrivacyCamGlyph.Foreground = p.CameraInUse ? Res("GreenBrush") : Res("MutedBrush");
    }

    private void PrivacySettings_Click(object sender, RoutedEventArgs e) =>
        OpenUri(_privacy?.Current.CameraInUse == true && _privacy.Current.MicrophoneInUse != true
            ? "ms-settings:privacy-webcam" : "ms-settings:privacy-microphone");

    // ----------------------------------------------------------- now playing
    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NowPlayingService.Position):
            case nameof(NowPlayingService.PositionSeconds):
            case nameof(NowPlayingService.PositionText):
                if (!_seeking) SeekBar.Value = _nowPlaying.PositionSeconds; // the 2×/s tick: cheap path only
                return;
        }
        UpdateMediaControls();
        RefreshAll();
        if (e.PropertyName is nameof(NowPlayingService.Title) or nameof(NowPlayingService.Artist) or nameof(NowPlayingService.IsPlaying))
        {
            // Titles, artists and play state arrive as separate events — settle before announcing.
            _trackTimer.Stop();
            _trackTimer.Start();
        }
    }

    private string? TrackKey() => _nowPlaying.HasSession ? $"{_nowPlaying.Title}|{_nowPlaying.Artist}" : null;

    private void AnnounceTrack()
    {
        if (!_mediaReady || !_nowPlaying.IsPlaying || TrackKey() is not { } key || key == _lastTrack) return;
        _lastTrack = key;
        var detail = string.IsNullOrEmpty(_nowPlaying.Artist) ? _nowPlaying.Title : $"{_nowPlaying.Title} — {_nowPlaying.Artist}";
        Notify("media", "", "Now playing", detail, art: _nowPlaying.Artwork);
    }

    private void UpdateMediaControls()
    {
        PlayButton.Content = _nowPlaying.IsPlaying ? "" : "";
        if (!_seeking)
        {
            SeekBar.Maximum = Math.Max(1, _nowPlaying.DurationSeconds);
            SeekBar.Value = _nowPlaying.PositionSeconds;
        }
        SeekBar.IsEnabled = _nowPlaying.CanSeek;
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _nowPlaying.TogglePlayPauseAsync();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await _nowPlaying.PreviousAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _nowPlaying.NextAsync();
    private void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _seeking = true;
    private async void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => await FinishSeekAsync();
    private async void SeekBar_LostMouseCapture(object sender, MouseEventArgs e) => await FinishSeekAsync();

    private async Task FinishSeekAsync()
    {
        if (!_seeking) return;
        _seeking = false;
        await _nowPlaying.SeekAsync(TimeSpan.FromSeconds(SeekBar.Value));
    }

    // ---------------------------------------------------------------- volume
    private void OnVolumeChanged(bool external)
    {
        UpdateVolumeUi();
        if (external && _audio is { Available: true } a)
            Notify("volume", VolumeGlyph(), a.Muted ? "Muted" : "Volume", a.Muted ? "" : $"{Math.Round(a.Volume * 100)}%",
                a.Muted ? 0 : a.Volume);
    }

    private void UpdateVolumeUi()
    {
        if (_audio is not { Available: true } a) return;
        int percent = (int)Math.Round(a.Volume * 100);
        _updatingVolume = true;
        VolumeSlider.Value = percent;
        _updatingVolume = false;
        var glyph = VolumeGlyph();
        VolumeValue.Text = a.Muted ? "Muted" : $"{percent}%";
        MuteButton.Content = glyph;
        VolumeToolGlyph.Text = glyph;
        VolumeToolText.Text = a.Muted ? "Muted" : $"{percent}%";
    }

    private string VolumeGlyph() => _audio is not { } a || a.Muted ? ""
        : a.Volume < 0.01 ? "" : a.Volume < 0.34 ? "" : a.Volume < 0.67 ? "" : "";

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingVolume) _audio?.SetVolume((float)(e.NewValue / 100));
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => _audio?.ToggleMute();
    private void SoundSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:sound");

    // ------------------------------------------------------------ screenshot
    // The notch collapses first so it isn't in the way of the capture.
    private void SnipRect_Click(object sender, RoutedEventArgs e) { SetMode(Mode.Peek); _screenshots?.Rectangle(); }
    private void SnipWindow_Click(object sender, RoutedEventArgs e) { SetMode(Mode.Peek); _screenshots?.Window(); }
    private void SnipApp_Click(object sender, RoutedEventArgs e) { SetMode(Mode.Peek); Try(ScreenshotService.OpenApp); }

    private async void SnipFull_Click(object sender, RoutedEventArgs e)
    {
        SetMode(Mode.Peek);
        Opacity = 0; // keep the notch itself out of the picture
        await Task.Delay(350);
        _screenshots?.FullScreen();
        await Task.Delay(900);
        Opacity = 1;
    }

    private void OnScreenshotSaved(string path) =>
        Notify("screenshot", "", "Screenshot saved", Path.GetFileName(path), accent: Res("AccentBrush"));

    // ------------------------------------------------------------- wallpaper
    private void BuildWallpaperStyles()
    {
        foreach (var style in new[] { WallpaperStyle.Fill, WallpaperStyle.Fit, WallpaperStyle.Stretch,
                                      WallpaperStyle.Tile, WallpaperStyle.Center, WallpaperStyle.Span })
        {
            var pill = new Button { Content = style.ToString(), Style = (Style)FindResource("OptionPill") };
            pill.Click += async (_, _) =>
            {
                try { await WallpaperService.SetStyle(style); }
                catch (Exception ex) { WallpaperMessage($"Couldn't change the fit: {ex.Message}"); }
                await RefreshWallpaperPanelAsync();
            };
            WallpaperStyles.Children.Add(pill);
        }
    }

    private async Task RefreshWallpaperPanelAsync()
    {
        _wallpaperPath = WallpaperService.Current();
        WallpaperName.Text = _wallpaperPath is null ? "No picture (colour or Spotlight)" : Path.GetFileName(_wallpaperPath);
        WallpaperName.ToolTip = _wallpaperPath;
        var style = WallpaperService.CurrentStyle()?.ToString();
        foreach (Button pill in WallpaperStyles.Children)
            pill.Tag = (string)pill.Content == style ? "on" : null;

        var recent = _settings.RecentWallpapers.Where(File.Exists).Take(5).ToList();
        RecentLabel.Visibility = Show(recent.Count > 0);
        RecentWallpapers.Children.Clear();
        foreach (var path in recent)
        {
            var thumb = new Border { Width = 64, Height = 36, CornerRadius = new CornerRadius(6), Background = Res("FillBrush") };
            var button = new Button { Style = (Style)FindResource("DockIcon"), Content = thumb, Margin = new Thickness(0, 0, 6, 0), ToolTip = Path.GetFileName(path) };
            button.Click += async (_, _) => await ApplyWallpaperAsync(path);
            RecentWallpapers.Children.Add(button);
            _ = LoadThumbAsync(thumb, path);
        }
        if (_mode == Mode.Expanded) ResizePill();

        WallpaperPreview.ImageSource = _wallpaperPath is null ? null : await WallpaperService.Thumbnail(_wallpaperPath, 300);
    }

    private static async Task LoadThumbAsync(Border target, string path)
    {
        if (await WallpaperService.Thumbnail(path, 128) is { } image)
            target.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    }

    private async Task ApplyWallpaperAsync(string path)
    {
        try
        {
            await WallpaperService.Apply(path, WallpaperService.CurrentStyle() ?? WallpaperStyle.Fill);
            _settings.RecentWallpapers.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            _settings.RecentWallpapers.Insert(0, path);
            if (_settings.RecentWallpapers.Count > 10) _settings.RecentWallpapers.RemoveRange(10, _settings.RecentWallpapers.Count - 10);
            SettingsService.Save(_settings);
            WallpaperMessage($"Wallpaper changed to {Path.GetFileName(path)}.");
            Notify("wallpaper", "", "Wallpaper changed", Path.GetFileName(path), accent: Res("AccentBrush"));
        }
        catch (Exception ex)
        {
            WallpaperMessage($"Couldn't set the wallpaper: {ex.Message}");
        }
        await RefreshWallpaperPanelAsync();
    }

    private void WallpaperMessage(string text)
    {
        WallpaperStatus.Text = text;
        WallpaperStatus.Visibility = Visibility.Visible;
        if (_mode == Mode.Expanded) ResizePill();
    }

    private async void WallpaperBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a wallpaper",
            Filter = "Pictures|" + string.Join(";", WallpaperService.Extensions.Select(x => "*" + x)) + "|All files|*.*",
            InitialDirectory = Path.GetDirectoryName(_wallpaperPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (Modal(() => dialog.ShowDialog(this)) == true) await ApplyWallpaperAsync(dialog.FileName);
    }

    private async void WallpaperNext_Click(object sender, RoutedEventArgs e)
    {
        if (WallpaperService.Next(_wallpaperPath, _settings.RecentWallpapers) is { } next) await ApplyWallpaperAsync(next);
        else WallpaperMessage("No other pictures in this folder or in Recent — use Change… to pick one.");
    }

    private void WallpaperPreview_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:personalization-background");

    /// <summary>Wallpaper changed elsewhere (Settings, another app): keep the open panel truthful.</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop)
            Dispatcher.BeginInvoke(() => { if (_panel == "wallpaper") _ = RefreshWallpaperPanelAsync(); });
    }
}
