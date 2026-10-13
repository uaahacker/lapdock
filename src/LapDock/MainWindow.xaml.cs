using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
/// <item>Expanded — on hover: status, pinned apps, media, tools and their panels.</item>
/// </list>
/// This file is the shell (states, hover, placement); MainWindow.Dock.cs holds the pinned apps,
/// MainWindow.Widgets.cs the system widgets.
/// </summary>
public partial class MainWindow : Window
{
    private enum Mode { Peek, Activity, Expanded }

    private const double PeekMinWidth = 190, PeekHeight = 32;
    private const double ExpandedMinWidth = 440;

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private Mode _mode = Mode.Peek;
    private double _expandedWidth = ExpandedMinWidth;
    private (double W, double H) _pillTarget = (PeekMinWidth, PeekHeight);
    private int _modal;       // > 0 while a file picker or similar dialog is open
    private bool _dragInside;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        BuildDock();

        // Grace period before closing, so crossing a tiny gap doesn't snap it shut. While a menu,
        // slider drag or dialog is active, keep re-checking instead of closing under the user.
        _collapseTimer.Tick += (_, _) =>
        {
            if (!CanCollapse()) return;
            _collapseTimer.Stop();
            if (!Island.IsMouseOver) SetMode(Mode.Peek);
        };

        SourceInitialized += (_, _) =>
        {
            HideFromAltTab();
            PlaceOnScreen(); // before first show, so it never flashes elsewhere
        };
        Loaded += async (_, _) => await StartWidgetsAsync();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; // static event: must unhook
            _collapseTimer.Stop();
            StopWidgets();
            SettingsService.Save(_settings);
        };
    }

    // ------------------------------------------------------------- states
    private void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        SetMode(Mode.Expanded);
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e) => RestartCollapse();

    private void RestartCollapse()
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private bool CanCollapse() =>
        !AnyMenuOpen() && !_dragInside && _modal == 0 && Mouse.Captured is null;

    private void SetMode(Mode mode)
    {
        if (mode != Mode.Expanded && _panel is not null) ShowPanel(null); // closing always closes the open panel
        if (mode == _mode) return;
        _mode = mode;
        if (mode != Mode.Expanded) Magnify(null);
        Fade(Peek, mode == Mode.Peek ? 1 : 0, mode == Mode.Peek ? 160 : 80, delayMs: mode == Mode.Peek ? 120 : 0);
        Fade(Activity, mode == Mode.Activity ? 1 : 0, mode == Mode.Activity ? 180 : 90, delayMs: mode == Mode.Activity ? 110 : 0);
        Fade(Expanded, mode == Mode.Expanded ? 1 : 0, mode == Mode.Expanded ? 200 : 90, delayMs: mode == Mode.Expanded ? 110 : 0);
        Expanded.IsHitTestVisible = mode == Mode.Expanded;
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
            Mode.Expanded => (_expandedWidth, Expanded.DesiredSize.Height),
            Mode.Activity => (Math.Max(PeekMinWidth, Activity.DesiredSize.Width), Activity.Height),
            _ => (Math.Max(PeekMinWidth, Peek.DesiredSize.Width), PeekHeight),
        };
        // Widgets refresh often (battery, clock, media); only animate when the size really changes —
        // every animation repaints the whole layered window.
        if (Math.Abs(w - _pillTarget.W) < 0.5 && Math.Abs(h - _pillTarget.H) < 0.5) return;
        _pillTarget = (w, h);
        bool growing = h > Pill.ActualHeight || w > Pill.ActualWidth;
        // Grow with a little spring (iOS), shrink quick and clean.
        IEasingFunction ease = growing
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(growing ? 340 : 230);
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(w, duration) { EasingFunction = ease });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, duration) { EasingFunction = ease });
    }

    private static void Fade(UIElement element, double to, int ms, int delayMs) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs)
        });

    private bool AnyMenuOpen() =>
        Island.ContextMenu.IsOpen || _dockButtons.Any(b => b.ContextMenu?.IsOpen == true);

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
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(PlaceOnScreen);

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
