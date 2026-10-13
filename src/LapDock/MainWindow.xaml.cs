using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LapDock.Models;
using LapDock.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LapDock;

/// <summary>
/// LapDock is a notch: a black pill flush with the top edge of a display.
/// <list type="bullet">
/// <item>Peek — collapsed: clock or now playing, privacy dots, battery.</item>
/// <item>Activity — a few seconds of "Charging", "Microphone in use", "Screenshot saved"…</item>
/// <item>Expanded — on hover: status chips and the tabs (Controls, Today, Stats…).</item>
/// </list>
/// This file is the shell (states, hover, placement); /// MainWindow.Widgets.cs the system widgets, MainWindow.Tabs.cs the tabs and the Settings window.
/// </summary>
public partial class MainWindow : Window
{
    private enum Mode { Peek, Activity, Expanded }

    private const double PeekMinWidth = 190, PeekHeight = 32;
    private const double QuietWidth = 124, QuietHeight = 24; // the plain black pill over fullscreen apps
    // The transparent window is recomposited whole for every animation frame (clock, Nova's blink, music bars),
    // so it's only tall while open: a 64 px strip otherwise.
    private const double OpenWindowHeight = 800, ClosedWindowHeight = 64, ActivityWindowHeight = 120; // a message banner is taller
    private const double ExpandedWidth = 560; // room for the tab bar and two columns of quick-settings tiles

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _dwellTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly DispatcherTimer _shrinkTimer = new() { Interval = TimeSpan.FromMilliseconds(320) }; // after the close animation
    private FullscreenWatcher? _fullscreen;

    private Mode _mode = Mode.Peek;
    private (double W, double H) _pillTarget = (PeekMinWidth, PeekHeight);
    private int _modal;       // > 0 while a file picker or similar dialog is open

    /// <summary>Just installed: say hello once the notch is up.</summary>
    public bool GreetOnStart { get; init; }

    /// <summary>A fullscreen app (video, game, slideshow) is on this display: stay out of the way.</summary>
    private bool Quiet => _fullscreen?.IsFullscreen == true;

    /// <summary>"Hide when covered": an app window is under the notch (or one is fullscreen), so it's slid away.</summary>
    private bool Hidden => _settings.NotchSpace == "AutoHide" && (_fullscreen?.IsTopCovered == true || Quiet);

    /// <summary>Out of the way (black pill, slid away, or the PC is locked): nothing animates, nothing pops up.</summary>
    private bool Away => Quiet || Hidden || _locked;

    private bool _locked;

    private AppBarService? _appBar;
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _revealHits;

    public MainWindow()
    {
        InitializeComponent();
        Pill.SizeChanged += (_, _) => ClipToPill(); // every frame of the open/close animation
        _settings = SettingsService.Load();

        // Grace period before closing, so crossing a tiny gap doesn't snap it shut. While a menu,
        // slider drag or dialog is active, keep re-checking instead of closing under the user.
        _collapseTimer.Tick += (_, _) =>
        {
            if (!CanCollapse()) return;
            _collapseTimer.Stop();
            // After a file drop WPF's idea of the mouse can be stale: ask where the cursor really is.
            if (!Island.IsMouseOver || !CursorOnPill()) SetMode(Mode.Peek);
        };
        _shrinkTimer.Tick += (_, _) =>
        {
            _shrinkTimer.Stop();
            if (_mode != Mode.Expanded) Height = _mode == Mode.Activity ? ActivityWindowHeight : ClosedWindowHeight;
        };
        _revealTimer.Tick += (_, _) => CheckReveal();
        _dwellTimer.Tick += (_, _) =>
        {
            _dwellTimer.Stop();
            if (Island.IsMouseOver) SetMode(Mode.Expanded);
        };

        SourceInitialized += (_, _) =>
        {
            HideFromAltTab();
            PlaceOnScreen(); // before first show, so it never flashes elsewhere
        };
        Loaded += async (_, _) =>
        {
            await StartWidgetsAsync();
            StartTabs(); // after the widgets: a "hotkey unavailable" message needs the activity timer wired
            _fullscreen = new FullscreenWatcher(this);
            _fullscreen.Changed += OnFullscreenChanged;
            OnFullscreenChanged();
            ApplyNotchSpace();
            if (GreetOnStart)
                Notify("hello", "nova", "LapDock is ready", $"Hover up here any time · {_settings.SummonHotkey} opens Settings", seconds: 6);
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Deactivated += (_, _) => { if (!Island.IsMouseOver) RestartCollapse(); }; // clicked away while typing

        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; // static event: must unhook
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _collapseTimer.Stop();
            _dwellTimer.Stop();
            _revealTimer.Stop();
            _fullscreen?.Dispose();
            _appBar?.Dispose(); // give the reserved strip back
            StopWidgets();
            StopTabs();
            SettingsService.Save(_settings);
        };
    }

    // ------------------------------------------------------------- states
    private void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        // A banner that opens something (a message, a download): hovering holds it up so it can be clicked.
        if (_mode == Mode.Activity && _activityClick is not null) { _activityTimer.Stop(); return; }
        // Over a fullscreen video, brushing the top edge mustn't pop the notch open: ask for a short hover.
        if (Quiet && _mode != Mode.Expanded) { _dwellTimer.Start(); return; }
        SetMode(Mode.Expanded);
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _dwellTimer.Stop();
        if (_mode == Mode.Activity && _activityClick is not null)
        {
            _activityTimer.Interval = TimeSpan.FromSeconds(1.5);
            _activityTimer.Start();
            return;
        }
        RestartCollapse();
    }

    // ------------------------------------------------------------ the shelf
    private DateTime _dragAt; // something is being dragged over the notch: don't close under it

    /// <summary>Files, links or text dragged onto the notch: it opens on the Shelf, ready for the drop.</summary>
    private void Island_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ShelfPage.IsDraggingOut || Away || !Views.Tabs.ShelfTab.Accepts(e.Data))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        _dragAt = DateTime.UtcNow;
        _collapseTimer.Stop();
        if (_mode != Mode.Expanded || Tabs.SelectedItem != ShelfTabItem)
        {
            Tabs.SelectedItem = ShelfTabItem;
            SetMode(Mode.Expanded);
        }
        ShelfPage.ShowDropTarget(true);
    }

    private void Island_DragLeave(object sender, DragEventArgs e)
    {
        ShelfPage.ShowDropTarget(false); // moving between parts of the notch: the next DragOver lights it again
        RestartCollapse();
    }

    private void Island_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        _dragAt = DateTime.MinValue;
        if (!ShelfPage.IsDraggingOut && Views.Tabs.ShelfTab.Accepts(e.Data)) ShelfPage.Add(e.Data);
        RestartCollapse(); // stays open while the mouse is on it
    }

    private bool CursorOnPill()
    {
        if (!GetCursorPos(out var p)) return true;
        var at = Pill.PointFromScreen(new Point(p.X, p.Y));
        return at.X >= 0 && at.Y >= 0 && at.X <= Pill.ActualWidth && at.Y <= Pill.ActualHeight;
    }

    /// <summary>
    /// A fullscreen app on this display: the notch becomes a plain black pill, like the iPhone island
    /// during a video — no clock, no music title, no live messages, nothing moving. Hovering still opens it.
    /// </summary>
    private void OnFullscreenChanged()
    {
        if (Away && _mode == Mode.Activity) SetMode(Mode.Peek);
        PeekNova.Paused = _mode != Mode.Peek || Away;
        Fade(Peek, _mode == Mode.Peek && !Away ? 1 : 0, 160, delayMs: 0);
        RefreshPeek(); // stops the music bars and Nova's dance while away, and resizes the pill
        _revealTimer.IsEnabled = Hidden && _mode == Mode.Peek;
    }

    /// <summary>The "When apps reach the top" setting: reserve the strip, slide away, or stay on top.</summary>
    private void ApplyNotchSpace()
    {
        if (_settings.NotchSpace == "Reserve")
        {
            if (_appBar is null)
            {
                _appBar = new AppBarService(this);
                _appBar.Tripped += () =>
                {
                    _settings.NotchSpace = "Overlay"; // safe for good; "Make room" can be chosen again in Settings
                    SettingsService.Save(_settings);
                    Notify("space", "settings", "Make room turned off", "Windows kept moving the reserved strip, so LapDock gave it back", seconds: 6);
                };
            }
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            _appBar.Reserve(ScreenService.ScreenOf(this).Bounds, (int)Math.Ceiling(PeekHeight * scale));
        }
        else _appBar?.Release();
        OnFullscreenChanged(); // Hidden may have changed with the setting
    }

    /// <summary>
    /// Slid away: the notch opens when the mouse is pushed against the top edge, at its middle, for about
    /// a third of a second (brushing past doesn't count) — like an auto-hiding menu bar.
    /// </summary>
    private void CheckReveal()
    {
        if (!Hidden || _mode != Mode.Peek) { _revealTimer.Stop(); _revealHits = 0; return; }
        if (!GetCursorPos(out var p)) return;
        var screen = ScreenService.ScreenOf(this).Bounds;
        var hwnd = new WindowInteropHelper(this).Handle;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        GetWindowRect(hwnd, out var r);
        int mid = (r.Left + r.Right) / 2, reach = (int)(PeekMinWidth / 2 * scale);
        bool atEdge = p.Y <= screen.Top + 2 && Math.Abs(p.X - mid) <= reach;
        _revealHits = atEdge ? _revealHits + 1 : 0;
        if (_revealHits < 3) return;
        _revealHits = 0;
        _revealTimer.Stop();
        SetMode(Mode.Expanded);
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WINRECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WINRECT rect);

    private void RestartCollapse()
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private bool CanCollapse() =>
        !AnyMenuOpen() && _modal == 0 && Mouse.Captured is null && !Typing() && DateTime.UtcNow - _dragAt > TimeSpan.FromMilliseconds(600);

    /// <summary>Typing a to-do: stay open until the text box loses focus (Esc, Enter… or clicking elsewhere).</summary>
    private bool Typing() => IsActive && Keyboard.FocusedElement is System.Windows.Controls.TextBox;

    private void SetMode(Mode mode)
    {
        if (mode != Mode.Expanded && _panel is not null) ShowPanel(null); // closing always closes the open panel
        if (mode == _mode) return;
        _mode = mode;
        if (mode != Mode.Activity)
        {
            _activityClick = null; // a banner's click only lives as long as the banner
            Activity.IsHitTestVisible = false;
        }
        PeekNova.Paused = mode != Mode.Peek || Away; // Nova only moves while the collapsed pill shows it
        _revealTimer.IsEnabled = Hidden && mode == Mode.Peek;
        if (mode == Mode.Expanded)
        {
            _shrinkTimer.Stop();
            Height = OpenWindowHeight; // room to open into, before anything animates
        }
        else if (mode == Mode.Activity && Height < ActivityWindowHeight)
        {
            _shrinkTimer.Stop();
            Height = ActivityWindowHeight;
        }
        else _shrinkTimer.Start();
        OnTabsModeChanged();
        Fade(Peek, mode == Mode.Peek && !Away ? 1 : 0, mode == Mode.Peek ? 160 : 80, delayMs: mode == Mode.Peek ? 120 : 0);
        Fade(Activity, mode == Mode.Activity ? 1 : 0, mode == Mode.Activity ? 180 : 90, delayMs: mode == Mode.Activity ? 110 : 0);
        Fade(Expanded, mode == Mode.Expanded ? 1 : 0, mode == Mode.Expanded ? 200 : 90, delayMs: mode == Mode.Expanded ? 110 : 0);
        Expanded.IsHitTestVisible = mode == Mode.Expanded;
        SlideFromTop(ExpandedDrop, mode == Mode.Expanded);
        SlideFromTop(ActivityDrop, mode == Mode.Activity);
        if (mode == Mode.Peek) RefreshPeek(); // a live activity's clock kept running meanwhile
        ResizePill();
    }

    /// <summary>Animates the pill to fit whatever the current state shows (it changes as panels open, music starts…).</summary>
    private void ResizePill()
    {
        // Run the pending layout pass first: measuring an element directly returns a stale size when
        // only its descendants changed (a panel opened, a text got longer) — the pill came out too short.
        UpdateLayout();
        var (w, h) = _mode switch
        {
            Mode.Expanded => (ExpandedWidth, Expanded.DesiredSize.Height),
            Mode.Activity => (Math.Max(PeekMinWidth, Activity.DesiredSize.Width), Math.Max(Activity.MinHeight, Activity.DesiredSize.Height)),
            _ when Hidden => (PeekMinWidth, 0), // slides up into the top edge
            _ when Quiet => (QuietWidth, QuietHeight),
            _ => (Math.Max(PeekMinWidth, Peek.DesiredSize.Width), PeekHeight),
        };
        // Widgets refresh often (battery, clock, media); only animate when the size really changes —
        // every animation repaints the whole layered window.
        if (Math.Abs(w - _pillTarget.W) < 0.5 && Math.Abs(h - _pillTarget.H) < 0.5) return;
        _pillTarget = (w, h);
        bool growing = h > Pill.ActualHeight || w > Pill.ActualWidth;
        // Opening: it widens, then drops down from the top edge and settles with a small spring.
        // Closing: quick and clean, straight back up.
        var out_ = new CubicEase { EasingMode = EasingMode.EaseOut };
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(growing ? 280 : 220)) { EasingFunction = out_ });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(growing ? 420 : 220))
        {
            EasingFunction = growing ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 } : out_,
        });
    }

    /// <summary>
    /// Nothing is ever drawn outside the black shape: while it grows, the content is revealed from the top
    /// edge downward (and while it shrinks, cut away) instead of spilling over the desktop.
    /// </summary>
    private void ClipToPill()
    {
        double w = Pill.ActualWidth, h = Pill.ActualHeight, r = Math.Min(h / 2, 26);
        foreach (var layer in new FrameworkElement[] { Peek, Activity, Expanded })
            layer.Clip = new RectangleGeometry(new Rect((layer.ActualWidth - w) / 2, 0, w, h), r, r);
    }

    /// <summary>Content drops in from the top edge as the notch opens, and lifts away as it closes.</summary>
    private static void SlideFromTop(TranslateTransform shift, bool show) =>
        shift.BeginAnimation(TranslateTransform.YProperty, show
            ? new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(380))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
            }
            : new DoubleAnimation(-8, TimeSpan.FromMilliseconds(110)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

    private static void Fade(UIElement element, double to, int ms, int delayMs) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs)
        });

    private bool AnyMenuOpen() => Island.ContextMenu.IsOpen;

    /// <summary>Runs a modal dialog without the notch collapsing underneath it.</summary>
    private T Modal<T>(Func<T> dialog)
    {
        _modal++;
        try { return dialog(); }
        finally
        {
            _modal--;
            RestartCollapse();
        }
    }

    // --------------------------------------------------------------- screens
    private void PlaceOnScreen()
    {
        var screen = ScreenService.FindByDeviceName(_settings.ScreenDeviceName);
        if (screen is not null) ScreenService.PlaceTopCenter(this, screen);
    }

    private void NextDisplay_Click(object sender, RoutedEventArgs e)
    {
        var screens = Forms.Screen.AllScreens;
        var current = ScreenService.ScreenOf(this).DeviceName;
        int next = (Array.FindIndex(screens, s => s.DeviceName == current) + 1) % screens.Length;
        _settings.ScreenDeviceName = screens[next].DeviceName;
        SettingsService.Save(_settings);
        PlaceOnScreen();
    }

    /// <summary>Monitor plugged/unplugged, resolution or scaling changed: back to the top of the preferred display.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        PlaceOnScreen();
        _fullscreen?.Check();
        if (_appBar?.IsReserved == true) ApplyNotchSpace(); // new resolution or display: reserve there
    });

    /// <summary>Locked: music may play on, but nobody sees the bars or a live clock — stop them.</summary>
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)) return;
        Dispatcher.BeginInvoke(() =>
        {
            _locked = e.Reason == SessionSwitchReason.SessionLock;
            OnFullscreenChanged();
        });
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- interop
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    /// <summary>A notch is not an app window: keep it out of Alt+Tab (ShowInTaskbar only covers the taskbar).</summary>
    private void HideFromAltTab()
    {
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
    }
}
