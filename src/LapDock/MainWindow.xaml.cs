using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LapDock.Controls;
using LapDock.Models;
using LapDock.Services;
using LapDock.ViewModels;
using Forms = System.Windows.Forms;

namespace LapDock;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern void LockWorkStation();

    private readonly MainViewModel _vm = new();
    private readonly CursorService _cursor = new();
    private readonly SystemStatsService _stats = new();
    private readonly AppSettings _settings;
    private readonly List<Button> _dockButtons = new();
    private readonly Random _rng = new();

    private List<Forms.Screen> _screens = new();
    private bool _initialising = true;
    private bool _musicPlaying;
    private bool _fullscreen;
    private int _tick;

    private static readonly string[] IdleBubbles =
    {
        "👀 I'm watching your cursor…",
        "👋 Hi! I'm Nova. I live on this screen.",
        "🎯 Start a Pomodoro and I'll guard your focus.",
        "🎵 Put some music on — I dance, honestly.",
        "💡 Tip: click a screen above to move me to your laptop panel.",
        "✨ Clean desk, clean mind. Your main screen is for work — I'm the vibe."
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _settings = SettingsService.Load();

        BuildScreenPicker();
        BuildDock();

        NotesBox.Text = _settings.Notes;
        _initialising = false;

        _cursor.CursorMoved += pos => Nova.LookAt(pos, _cursor.IdleSeconds);
        _cursor.Start();

        var statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        statsTimer.Tick += (_, _) => { UpdateStats(); UpdateMoodLine(); };
        statsTimer.Start();
        UpdateStats();

        var eqTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        eqTimer.Tick += (_, _) => AnimateEqualizer();
        eqTimer.Start();

        _vm.PomodoroPhaseChanged += isFocus =>
        {
            BubbleText.Text = isFocus ? "🎯 Back to focus — you got this." : "☕ Break time. Stretch!";
            System.Media.SystemSounds.Asterisk.Play();
        };

        Loaded += async (_, _) =>
        {
            // Restore LapDock onto its saved display (usually the laptop panel).
            var target = ScreenService.FindByDeviceName(_settings.ScreenDeviceName);
            if (target is not null)
            {
                SelectScreenInPicker(target);
                ScreenService.MoveToScreen(this, target);
            }
            await LoadWeatherAsync();
        };

        Closed += (_, _) =>
        {
            _cursor.Dispose();
            SettingsService.Save(_settings);
        };
    }

    // ------------------------------------------------------------ screen pick
    private void BuildScreenPicker()
    {
        _screens = ScreenService.AllScreens.ToList();
        ScreenPicker.Items.Clear();
        for (int i = 0; i < _screens.Count; i++)
            ScreenPicker.Items.Add(ScreenService.FriendlyName(_screens[i], i));
        ScreenPicker.SelectedIndex = Math.Max(0, _screens.FindIndex(s => s.Primary));
    }

    private void SelectScreenInPicker(Forms.Screen screen)
    {
        int idx = _screens.IndexOf(screen);
        if (idx >= 0) ScreenPicker.SelectedIndex = idx;
    }

    private void ScreenPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialising || ScreenPicker.SelectedIndex < 0) return;
        var screen = _screens[ScreenPicker.SelectedIndex];
        _settings.ScreenDeviceName = screen.DeviceName;
        SettingsService.Save(_settings);
        ScreenService.MoveToScreen(this, screen);
        BubbleText.Text = $"🖥️ Moved to {ScreenService.FriendlyName(screen, ScreenPicker.SelectedIndex)}";
    }

    // ------------------------------------------------------------------ dock
    private void BuildDock()
    {
        foreach (var pin in _settings.PinnedApps)
        {
            var parts = pin.Split('|', 2);
            if (parts.Length != 2) continue;
            var btn = new Button
            {
                Content = parts[0],
                Style = (Style)FindResource("DockButton"),
                ToolTip = parts[1]
            };
            var target = parts[1];
            btn.Click += (_, _) => Launch(target);
            btn.MouseEnter += (_, _) => Magnify(btn);
            btn.MouseLeave += (_, _) => Magnify(null);
            _dockButtons.Add(btn);
            DockPanel.Children.Add(btn);
        }
    }

    /// <summary>macOS-style magnification: hovered icon grows, neighbours grow a little.</summary>
    private void Magnify(Button? hovered)
    {
        for (int i = 0; i < _dockButtons.Count; i++)
        {
            double scale = 1.0;
            if (hovered is not null)
            {
                int d = Math.Abs(i - _dockButtons.IndexOf(hovered));
                scale = d == 0 ? 1.28 : d == 1 ? 1.13 : 1.0;
            }
            _dockButtons[i].RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    private void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            BubbleText.Text = $"⚠️ Couldn't open {target} — is it installed?";
        }
    }

    // ----------------------------------------------------------------- music
    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        MediaService.PlayPause();
        _musicPlaying = !_musicPlaying;
        PlayButton.Content = _musicPlaying ? "⏸" : "▶";
        MediaStatus.Text = _musicPlaying ? "  Now playing on this PC…" : "  Paused";
        Nova.SetMood(_musicPlaying ? NovaMood.Dancing : NovaMood.Awake);
        BubbleText.Text = _musicPlaying ? "🎵 Grooving… nice taste." : "👀 Back to watching your cursor…";
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => MediaService.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => MediaService.Next();
    private void VolUp_Click(object sender, RoutedEventArgs e) => MediaService.VolumeUp();
    private void VolDown_Click(object sender, RoutedEventArgs e) => MediaService.VolumeDown();
    private void Mute_Click(object sender, RoutedEventArgs e) => MediaService.Mute();

    private void AnimateEqualizer()
    {
        var bars = new[] { Eq1, Eq2, Eq3, Eq4, Eq5 };
        foreach (var bar in bars)
            bar.Height = _musicPlaying ? _rng.Next(6, 30) : 6;
    }

    // ---------------------------------------------------------------- timers
    private void PomoStartPause_Click(object sender, RoutedEventArgs e)
    {
        _vm.PomoStartPause();
        if (_vm.PomoIsRunning)
        {
            if (!_musicPlaying) Nova.SetMood(NovaMood.Focused);
            BubbleText.Text = "🎯 Focus mode — Nova is guarding your time.";
        }
        else if (!_musicPlaying) Nova.SetMood(NovaMood.Awake);
    }

    private void PomoReset_Click(object sender, RoutedEventArgs e) => _vm.PomoReset();
    private void CountStartPause_Click(object sender, RoutedEventArgs e) => _vm.CountStartPause();
    private void CountReset_Click(object sender, RoutedEventArgs e) => _vm.CountReset();
    private void WatchStartPause_Click(object sender, RoutedEventArgs e) => _vm.WatchStartPause();
    private void WatchReset_Click(object sender, RoutedEventArgs e) => _vm.WatchReset();

    // ----------------------------------------------------------------- stats
    private void UpdateStats()
    {
        var s = _stats.GetStats();
        CpuBar.Value = s.CpuPercent;
        CpuText.Text = $"CPU — {s.CpuPercent:0}%";
        RamBar.Value = s.RamPercent;
        RamText.Text = $"RAM — {s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB ({s.RamPercent:0}%)";
        if (s.BatteryPercent is int batt)
        {
            BattBar.Value = batt;
            BattText.Text = $"Battery — {batt}%{(s.BatteryCharging ? " ⚡ charging" : "")}";
        }
        else
        {
            BattBar.Value = 0;
            BattText.Text = "Battery — plugged in / none";
        }
    }

    private async Task LoadWeatherAsync()
    {
        var w = await WeatherService.GetCurrentAsync(_settings.Latitude, _settings.Longitude);
        if (w is { } info)
        {
            WeatherText.Text = $"{info.Summary}  {info.TempC:0}°C";
            WeatherCity.Text = _settings.CityName;
        }
        else
        {
            WeatherText.Text = "🌡️ offline";
        }
    }

    // ------------------------------------------------------------ mood line
    private void UpdateMoodLine()
    {
        _tick++;
        MoodText.Text = Nova.Mood switch
        {
            NovaMood.Sleepy => "Nova is sleepy — cursor has been still a while",
            NovaMood.Dancing => "Nova is dancing to your music",
            NovaMood.Happy => "Nova noticed you!",
            NovaMood.Focused => "Nova is in focus mode with you",
            _ => "Nova is awake, watching your cursor"
        };

        // Rotate idle bubbles every ~9 s, unless something more important is showing.
        if (_tick % 9 == 0 && Nova.Mood == NovaMood.Awake && !_vm.PomoIsRunning && !_musicPlaying)
            BubbleText.Text = IdleBubbles[_rng.Next(IdleBubbles.Length)];
    }

    // ---------------------------------------------------------- quick actions
    private void Lock_Click(object sender, RoutedEventArgs e) => LockWorkStation();
    private void Snip_Click(object sender, RoutedEventArgs e) => Launch("ms-screenclip:");
    private void TaskMgr_Click(object sender, RoutedEventArgs e) => Launch("taskmgr.exe");
    private void Focus_Click(object sender, RoutedEventArgs e) => Launch("ms-settings:quietmomentshome");
    private void DisplaySettings_Click(object sender, RoutedEventArgs e) => Launch("ms-settings:display");
    private void SoundSettings_Click(object sender, RoutedEventArgs e) => Launch("ms-settings:sound");

    // ----------------------------------------------------------------- notes
    private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialising) return;
        _settings.Notes = NotesBox.Text;
        SettingsService.Save(_settings);
    }

    // ----------------------------------------------------------- window chrome
    private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && WindowState != WindowState.Maximized)
            DragMove();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        BubbleText.Text = Topmost ? "📌 Pinned — I'll stay on top." : "📌 Unpinned.";
    }

    private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            Width = 1340; Height = 820;
            Left = (Forms.Screen.PrimaryScreen!.WorkingArea.Width - Width) / 2;
            Top = (Forms.Screen.PrimaryScreen!.WorkingArea.Height - Height) / 2;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && _fullscreen) ToggleFullscreen();
    }
}
