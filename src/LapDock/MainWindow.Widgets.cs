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
/// System widgets: clock, battery, privacy (mic/camera), now playing, volume, screenshot —
/// plus the short-lived "activity" messages they raise. Every widget is optional
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

    private string? _panel;            // open chip panel: snip | battery | privacy
    private string? _lastTrack;        // "title|artist" last announced
    private (string Text, DateTime At) _lastNotice;
    private bool _mediaReady;
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
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (_settings.ShowBattery)
            Try(() => { _battery = new BatteryService(Dispatcher); _battery.Changed += OnBatteryChanged; _battery.Start(); });
        if (_settings.MicrophoneIndicator || _settings.CameraIndicator)
            Try(() => { _privacy = new PrivacyService(Dispatcher); _privacy.Changed += OnPrivacyChanged; _privacy.Start(); });
        Try(() => { _audio = new AudioService(Dispatcher); _audio.Changed += OnVolumeChanged; _audio.Start(); });
        Try(() => { _screenshots = new ScreenshotService(Dispatcher); _screenshots.Saved += OnScreenshotSaved; _screenshots.Start(); });

        if (_settings.MediaControls)
        {
            _nowPlaying.PropertyChanged += OnNowPlayingChanged;
            await _nowPlaying.InitializeAsync(); // handles its own failures (SMTC unavailable → no media section)
            _lastTrack = TrackKey(); // don't announce what was already playing at startup
        }
        _mediaReady = true;
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
        _eqTimer.IsEnabled = media && !Quiet; // the only animation that runs on its own: only while music plays, never over fullscreen apps
        UpdateNova();
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


        if (_panel is not null) RefreshPanel(_panel);
        if (_mode == Mode.Expanded) ResizePill();
    }

    private void ShowPanel(string? name)
    {
        _panel = name;
        SnipPanel.Visibility = Show(name == "snip");
        BatteryPanel.Visibility = Show(name == "battery");
        PrivacyPanel.Visibility = Show(name == "privacy");
        PanelHost.Visibility = Show(name is not null);
        if (name is not null) RefreshPanel(name);
        if (_mode == Mode.Expanded) ResizePill();
    }

    private void TogglePanel(string name) => ShowPanel(_panel == name ? null : name);

    private void RefreshPanel(string name)
    {
        switch (name)
        {
            case "battery": UpdateBatteryPanel(); break;
            case "privacy": UpdatePrivacyPanel(); break;
        }
    }

    private void SnipChip_Click(object sender, RoutedEventArgs e) => TogglePanel("snip");
    private void BatteryChip_Click(object sender, RoutedEventArgs e) => TogglePanel("battery");
    private void PrivacyChip_Click(object sender, RoutedEventArgs e) => TogglePanel("privacy");

    internal static void OpenUri(string uri)
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
        Brush? accent = null, ImageSource? art = null, double seconds = 2.8)
    {
        if (!_settings.DynamicNotifications || _mode == Mode.Expanded || Quiet) return; // nothing pops up over a fullscreen app
        var text = title + "|" + detail;
        if (_mode != Mode.Activity && _lastNotice.Text == text && DateTime.UtcNow - _lastNotice.At < TimeSpan.FromSeconds(5)) return;
        _lastNotice = (text, DateTime.UtcNow);

        // glyph: one of LapDock's icon names ("volume"), or a single Segoe glyph for what the pack doesn't draw.
        bool named = glyph.Length > 1;
        ActivityGlyph.Text = named ? "" : glyph;
        ActivityGlyph.Foreground = accent ?? Res("TextBrush");
        ActivityGlyph.Visibility = Show(art is null && !named);
        ActivityIcon2.Icon = named ? glyph : null;
        ActivityIcon2.Foreground = accent ?? Res("TextBrush");
        ActivityIcon2.Visibility = Show(art is null && named);
        ActivityIcon.Background = art is null ? Res("FillBrush") : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        ActivityTitle.Text = title;
        ActivityDetail.Text = detail;
        ActivityDetail.Visibility = Show(!string.IsNullOrEmpty(detail));
        ActivityBar.Visibility = Show(progress is not null);
        ActivityBar.Value = progress ?? 0;
        ActivityBar.Foreground = accent ?? Res("TextBrush");

        _activityTimer.Stop();
        _activityTimer.Interval = TimeSpan.FromSeconds(seconds);
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
                    $"{now.Percent}%", level, Res("AccentBrush"));
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
                    Notify("battery", BatteryGlyph(now), "Fully charged", "100% — you can unplug", level, Res("AccentBrush")); break;
            }
        }
    }

    private void UpdateBatteryPanel()
    {
        var b = _battery?.Current ?? default;
        BatteryBig.Text = $"{b.Percent}%";
        BatteryStatus.Text = BatteryStatusText(b);
        BatteryBar.Value = b.Percent;
        BatteryBar.Foreground = b.State is BatteryState.Low or BatteryState.Critical ? BatteryBrush(b) : Res("AccentBrush");
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
        _ => b.Charging ? Res("AccentBrush") : Res("TextBrush"),
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
            Notify("camera", "", "Camera in use", string.Join(", ", now.Camera), accent: Res("AccentBrush"));
    }

    private void UpdatePrivacyPanel()
    {
        var p = _privacy?.Current ?? PrivacyState.None;
        PrivacyMicRow.Visibility = Show(_settings.MicrophoneIndicator);
        PrivacyMicText.Text = p.MicrophoneInUse ? "In use by " + string.Join(", ", p.Microphone) : "Not in use";
        PrivacyMicGlyph.Foreground = p.MicrophoneInUse ? Res("OrangeBrush") : Res("MutedBrush");
        PrivacyCamRow.Visibility = Show(_settings.CameraIndicator);
        PrivacyCamText.Text = p.CameraInUse ? "In use by " + string.Join(", ", p.Camera) : "Not in use";
        PrivacyCamGlyph.Foreground = p.CameraInUse ? Res("AccentBrush") : Res("MutedBrush");
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
                return; // the 2×/s tick: MusicCard moves its own seek bar
        }
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
        Notify("media", "music", "Now playing", detail, art: _nowPlaying.Artwork);
    }

    // ---------------------------------------------------------------- volume
    // The slider and mute live in the Controls tab; the notch only announces changes made elsewhere.
    private void OnVolumeChanged(bool external)
    {
        if (external && _audio is { Available: true } a)
            Notify("volume", "volume", a.Muted ? "Muted" : "Volume", a.Muted ? "" : $"{Math.Round(a.Volume * 100)}%",
                a.Muted ? 0 : a.Volume);
    }

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
    /// <summary>Wallpaper changed elsewhere (Settings, another app): keep the Wallpaper tab truthful.</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop)
            Dispatcher.BeginInvoke(() => { if (Tabs.SelectedItem == WallpaperTabItem) WallpaperPage.RefreshCurrent(); });
    }
}
