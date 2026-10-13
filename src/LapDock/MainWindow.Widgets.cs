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
/// System widgets: clock, battery (with history and health), privacy (mic/camera), now playing, volume,
/// screenshot, downloads, USB devices, sound output — plus what they show: the short-lived "activity"
/// messages, other apps' notifications, and live activities in the collapsed pill (a call, a timer or
/// focus session, a download). Every widget is optional (AppSettings flags) and starts in isolation,
/// so one failing never takes the notch down.
/// </summary>
public partial class MainWindow
{
    private readonly NowPlayingService _nowPlaying = new();
    private BatteryService? _battery;
    private PrivacyService? _privacy;
    private AudioService? _audio;
    private ScreenshotService? _screenshots;
    private DownloadWatcher? _downloads;
    private UsbWatcher? _usb;
    private AppNotificationService? _notices;

    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _eqTimer = new() { Interval = TimeSpan.FromMilliseconds(66) }; // ~15 frames a second
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private readonly DispatcherTimer _trackTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };       // a live activity's clock
    private readonly DispatcherTimer _batteryLogTimer = new() { Interval = TimeSpan.FromMinutes(10) }; // the chart's steady points

    private string? _panel;            // open chip panel: snip | battery | privacy
    private string? _lastTrack;        // "title|artist" last announced
    private (string Text, DateTime At) _lastNotice;
    private bool _mediaReady;
    private int _eqTick;
    private readonly double[] _eqGain = { 1, .8, 1, .8, 1 };
    private readonly Random _random = new();
    private Action? _activityClick;     // the shown activity opens something when clicked
    private string? _callApp;           // an app using the microphone for a call
    private DateTime _callSince;
    private int _heldNotices;           // app notifications that arrived during a focus session

    private Brush Res(string key) => (Brush)FindResource(key);

    private async Task StartWidgetsAsync()
    {
        _activityTimer.Tick += (_, _) => { _activityTimer.Stop(); if (_mode == Mode.Activity) SetMode(Mode.Peek); };
        _eqTimer.Tick += (_, _) => AnimateEq();
        _liveTimer.Tick += (_, _) => { if (_mode == Mode.Peek) RefreshPeek(); };
        _batteryLogTimer.Tick += (_, _) => { if (_battery is not null) BatteryHistory.Record(_battery.Current); };
        _trackTimer.Tick += (_, _) => { _trackTimer.Stop(); AnnounceTrack(); };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (_settings.ShowBattery)
            Try(() =>
            {
                _battery = new BatteryService(Dispatcher);
                _battery.Changed += OnBatteryChanged;
                _battery.Start();
                BatteryHistory.Prune();
                BatteryHistory.Record(_battery.Current);
                _batteryLogTimer.Start();
            });
        if (_settings.MicrophoneIndicator || _settings.CameraIndicator)
            Try(() => { _privacy = new PrivacyService(Dispatcher); _privacy.Changed += OnPrivacyChanged; _privacy.Start(); UpdateCall(_privacy.Current); });
        Try(() => { _audio = new AudioService(Dispatcher); _audio.Changed += OnVolumeChanged; _audio.OutputChanged += OnOutputChanged; _audio.Start(); });
        Try(() => { _screenshots = new ScreenshotService(Dispatcher); _screenshots.Saved += OnScreenshotSaved; _screenshots.Start(); });
        Try(() => { _downloads = new DownloadWatcher(Dispatcher); _downloads.Changed += RefreshPeek; _downloads.Completed += OnDownloaded; _downloads.Start(); });
        Try(() => { _usb = new UsbWatcher(Dispatcher); _usb.Connected += OnUsbConnected; _usb.Start(); });
        await StartNoticesAsync();

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
        foreach (var t in new[] { _clockTimer, _eqTimer, _activityTimer, _trackTimer, _liveTimer, _batteryLogTimer }) t.Stop();
        _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
        _nowPlaying.Dispose();
        _battery?.Dispose();
        _privacy?.Dispose();
        _audio?.Dispose();
        _screenshots?.Dispose();
        _downloads?.Dispose();
        _usb?.Dispose();
        _notices?.Dispose();
    }

    private void RefreshAll()
    {
        RefreshPeek();
        RefreshExpanded();
    }

    // ------------------------------------------------------------------ peek
    private void RefreshPeek()
    {
        var live = Live();
        bool media = _settings.MediaControls && _nowPlaying.IsPlaying && live is null;
        PeekLive.Visibility = Show(live is not null);
        PeekLiveValue.Visibility = Show(live is not null);
        if (live is { } l)
        {
            PeekLiveIcon.Icon = l.Icon;
            PeekLiveIcon.Foreground = l.Tint;
            PeekLiveLabel.Text = l.Label;
            PeekLiveValue.Text = l.Value;
            PeekLiveValue.Foreground = l.Tint;
        }
        _liveTimer.IsEnabled = live is not null && !Away;
        PeekArtBrush.ImageSource = _nowPlaying.Artwork;
        PeekArt.Visibility = Show(media && _nowPlaying.Artwork is not null);
        PeekTitle.Text = _nowPlaying.Title;
        PeekTitle.Visibility = Show(media);
        PeekEq.Visibility = Show(media);
        _eqTimer.IsEnabled = media && !Away; // the only animation that runs on its own: only while music plays, never over fullscreen apps
        UpdateNova();
        PeekTime.Visibility = Show(!media && live is null && _settings.ShowClock);

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

    /// <summary>
    /// The music bars follow what's actually playing: the output's left/right level, each bar with its own
    /// slowly wandering gain so they read like a spectrum — quick to jump, slower to fall, flat in silence.
    /// </summary>
    private void AnimateEq()
    {
        _eqTick++;
        var bars = PeekEq.Levels;
        if (_audio is not { HasMeter: true } audio)
        {
            for (int i = 0; i < bars.Length; i++) // no level meter on this device: a gentle wave
                bars[i] = .85 * Math.Abs(Math.Sin(_eqTick * .25 + i * .9));
        }
        else
        {
            var (left, right) = audio.Levels;
            for (int i = 0; i < bars.Length; i++)
            {
                if (_eqTick % 5 == i) _eqGain[i] = .55 + .45 * _random.NextDouble();
                double level = (i < 2 ? left : i > 2 ? right : (left + right) / 2) * _eqGain[i];
                double target = Math.Min(1, Math.Sqrt(level) * 1.2);
                bars[i] += (target - bars[i]) * (target > bars[i] ? .75 : .3);
            }
        }
        PeekEq.InvalidateVisual();
    }

    // ------------------------------------------------------------ live
    /// <summary>What the collapsed pill shows instead of the clock: a call, then a timer or focus session, then a download.</summary>
    private (string Icon, string Label, string Value, Brush Tint)? Live()
    {
        if (_callApp is { } app) return ("phone", app, Elapsed(DateTime.UtcNow - _callSince), Res("AccentBrush"));
        if (TodayPage.Countdown is { } c) return (c.Icon, c.Label, Elapsed(c.Left), c.Accent ? Res("AccentBrush") : Res("TextBrush"));
        if (_downloads?.Current is { } d)
            return ("download", d.Count > 1 ? $"{d.Name} +{d.Count - 1}" : d.Name, DownloadWatcher.FormatBytes(d.Bytes), Res("TextBrush"));
        return null;
    }

    /// <summary>"04:59", or "1:02:07" past an hour.</summary>
    public static string Elapsed(TimeSpan t)
    {
        int s = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }

    /// <summary>A timer or focus session started, paused or ended; a focus session's held-back notifications are summed up.</summary>
    private void OnCountdownChanged()
    {
        RefreshPeek();
        if (TodayPage.Focusing || _heldNotices == 0) return;
        int held = _heldNotices;
        _heldNotices = 0;
        Notify("app", "bell", held == 1 ? "1 notification while you focused" : $"{held} notifications while you focused",
            "Click to see them", seconds: 6, caption: "Focus", click: () => OpenUri("ms-actioncenter:"));
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
    /// <param name="click">Makes it a button: hovering keeps it up, clicking runs this (opens the app, the folder…).</param>
    /// <param name="caption">A small line above the title (an app notification's app name).</param>
    /// <param name="appIcon">An app's own icon, shown as is instead of the round glyph.</param>
    private void Notify(string key, string glyph, string title, string detail, double? progress = null,
        Brush? accent = null, ImageSource? art = null, double seconds = 2.8, Action? click = null, string? caption = null,
        ImageSource? appIcon = null)
    {
        bool enabled = key == "app" ? _settings.AppNotifications : _settings.DynamicNotifications;
        if (!enabled || _mode == Mode.Expanded || Away) return; // nothing pops up over a fullscreen app or a covering window
        var text = title + "|" + detail;
        if (_mode != Mode.Activity && _lastNotice.Text == text && DateTime.UtcNow - _lastNotice.At < TimeSpan.FromSeconds(5)) return;
        _lastNotice = (text, DateTime.UtcNow);

        // glyph: one of LapDock's icon names ("volume"), or a single Segoe glyph for what the pack doesn't draw.
        bool named = glyph.Length > 1, plain = art is null && appIcon is null;
        ActivityGlyph.Text = named ? "" : glyph;
        ActivityGlyph.Foreground = accent ?? Res("TextBrush");
        ActivityGlyph.Visibility = Show(plain && !named);
        ActivityIcon2.Icon = named ? glyph : null;
        ActivityIcon2.Foreground = accent ?? Res("TextBrush");
        ActivityIcon2.Visibility = Show(plain && named);
        ActivityAppIcon.Source = appIcon;
        ActivityAppIcon.Visibility = Show(appIcon is not null);
        ActivityIcon.Background = appIcon is not null ? Brushes.Transparent
            : art is null ? Res("FillBrush") : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        ActivityIcon.CornerRadius = new CornerRadius(appIcon is not null ? 9 : 17);
        // An app's notification: its name on top, then the sender / subject, then up to two lines of message.
        bool message = caption is not null;
        ActivityCaption.Text = caption ?? "";
        ActivityCaption.Visibility = Show(message);
        ActivityText.Margin = message ? new Thickness(0, 9, 22, 11) : new Thickness(0, 4, 20, 0);
        ActivityDetail.TextWrapping = message ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ActivityDetail.MaxWidth = message ? 300 : 230;
        ActivityDetail.MaxHeight = message ? 32 : double.PositiveInfinity;
        ActivityTitle.Text = title;
        ActivityDetail.Text = detail;
        ActivityDetail.Visibility = Show(!string.IsNullOrEmpty(detail));
        _activityClick = click;
        Activity.IsHitTestVisible = click is not null;
        Activity.Cursor = click is null ? null : Cursors.Hand;
        ActivityBar.Visibility = Show(progress is not null);
        ActivityBar.Value = progress ?? 0;
        ActivityBar.Foreground = accent ?? Res("TextBrush");

        _activityTimer.Stop();
        _activityTimer.Interval = TimeSpan.FromSeconds(seconds);
        _activityTimer.Start();
        if (_mode == Mode.Activity) ResizePill();
        else SetMode(Mode.Activity);
    }

    private void Activity_Click(object sender, MouseButtonEventArgs e)
    {
        if (_activityClick is not { } click) return;
        _activityClick = null;
        SetMode(Mode.Peek);
        click();
    }

    // ------------------------------------------------------- notifications
    /// <summary>Other apps' notifications (Settings: "Notifications from apps"); starts once, when switched on.</summary>
    private async Task StartNoticesAsync()
    {
        if (!_settings.AppNotifications || _notices is not null) return;
        _notices = new AppNotificationService();
        _notices.Arrived += OnAppNotice;
        if (!await _notices.StartAsync())
        {
            _notices.Dispose();
            _notices = null;
        }
    }

    private void OnAppNotice(AppNotice n)
    {
        if (TodayPage.Focusing)
        {
            _heldNotices += n.Count; // shown as a count when the session ends
            return;
        }
        var app = string.IsNullOrEmpty(n.AppId) ? null : @"shell:AppsFolder\" + n.AppId;
        Notify("app", "bell", n.Title, n.Body, seconds: 6,
            caption: n.Count > 1 ? $"{n.App} · {n.Count} new" : n.App,
            appIcon: app is null ? null : ShellIconService.Get(app, 64).Icon,
            click: app is null ? null : () => OpenUri(app));
    }

    // ------------------------------------------------------ downloads, devices
    private void OnDownloaded(string path) =>
        Notify("download", "download", "Downloaded", Path.GetFileName(path), accent: Res("AccentBrush"), seconds: 5,
            click: () => DownloadWatcher.ShowInFolder(path));

    private void OnUsbConnected(string name, DriveInfo? drive)
    {
        string detail = "Connected";
        if (drive is not null)
        {
            try
            {
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "USB drive" : drive.VolumeLabel;
                detail = $"{label} ({drive.Name.TrimEnd('\\')}) · {DownloadWatcher.FormatBytes(drive.AvailableFreeSpace)} free · click to open";
            }
            catch (Exception) { detail = $"Drive {drive.Name.TrimEnd('\\')} · click to open"; }
        }
        Notify("usb", "usb", name, detail, accent: Res("AccentBrush"), seconds: drive is null ? 3.5 : 6,
            click: drive is null ? null : () => OpenUri(drive.RootDirectory.FullName));
    }

    /// <summary>Windows switched the sound output: AirPods connected, back to the speakers…</summary>
    private void OnOutputChanged()
    {
        if (_audio?.DeviceName is not { } name) return;
        bool phones = _audio.IsHeadphones;
        Notify("output", phones ? "headphones" : "volume", AudioService.ShortName(name), phones ? "Connected" : "Sound output",
            accent: phones ? Res("AccentBrush") : null);
    }

    // ------------------------------------------------------------------ call
    /// <summary>Calling apps; a browser using the microphone is most likely a call too (Meet, Teams on the web…).</summary>
    private static readonly string[] CallApps =
        { "Teams", "Zoom", "Discord", "WhatsApp", "Skype", "Slack", "Telegram", "Signal", "Webex", "Phone Link", "Messenger", "Viber",
          "Chrome", "Edge", "Firefox", "Brave", "Opera" };

    public static bool IsCallApp(string app) => CallApps.Any(c => app.Contains(c, StringComparison.OrdinalIgnoreCase));

    private void UpdateCall(PrivacyState p)
    {
        var app = _settings.MicrophoneIndicator ? p.Microphone.FirstOrDefault(IsCallApp) : null;
        if (app is not null && _callApp is null) _callSince = DateTime.UtcNow;
        _callApp = app;
    }

    // --------------------------------------------------------------- battery
    private void OnBatteryChanged(BatteryInfo old, BatteryInfo now)
    {
        if (now.Percent != old.Percent || now.PluggedIn != old.PluggedIn) BatteryHistory.Record(now);
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

        var now = DateTime.Now;
        var slots = BatteryHistory.Slots(BatteryHistory.Load(now.AddHours(-24)), now);
        BatteryHistoryChart.Slots = slots;
        BatteryChartStart.Text = now.AddHours(-24).ToString("t");
        BatteryChartMiddle.Text = now.AddHours(-12).ToString("t");
        BatteryChartEmpty.Visibility = Show(slots.Count(s => s is not null) < 3);

        var health = BatteryHistory.Health();
        BatteryHealthRow.Visibility = Show(health is not null);
        if (health is not { } h) return;
        BatteryHealthText.Text = $"{h.Percent:0}%";
        BatteryHealthText.ToolTip = "How much charge the battery holds now, compared with when it was new";
        BatteryCapacityText.Text = $"{h.FullWh:0.#} of {h.DesignWh:0.#} Wh";
        BatteryRateLabel.Text = h.RateW > .05 ? "Charging at" : h.RateW < -.05 ? "Using" : "Power";
        BatteryRateText.Text = Math.Abs(h.RateW) > .05 ? $"{Math.Abs(h.RateW):0.#} W" : "—";
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
        UpdateCall(now);
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
