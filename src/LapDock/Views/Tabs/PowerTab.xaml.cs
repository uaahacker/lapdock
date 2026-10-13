using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LapDock.Controls;

namespace LapDock.Views.Tabs;

public enum PowerAction { Lock, Sleep, Restart, ShutDown }

/// <summary>
/// Power: Lock, Sleep, Restart, Shut down. Restart and Shut down only happen after a 0.6 s
/// press-and-hold (a ring fills around the button; letting go early cancels), so a stray click
/// can never turn the PC off. Keyboard: hold Enter or Space the same way.
/// </summary>
public partial class PowerTab : UserControl
{
    private static readonly Duration HoldTime = TimeSpan.FromMilliseconds(600);

    private sealed record Item(PowerAction Action, string Icon, string Label, string Hint, string? HoldBrush);

    private static readonly Item[] Items =
    {
        new(PowerAction.Lock, "lock", "Lock", "Win + L", null),
        new(PowerAction.Sleep, "moon", "Sleep", "Wakes in a second", null),
        new(PowerAction.Restart, "restart", "Restart", "Hold to restart", "AccentBrush"),
        new(PowerAction.ShutDown, "power", "Shut down", "Hold to shut down", "AccentBrush"),
    };

    private PowerAction? _holding;

    /// <summary>The user chose an action (for Restart / Shut down: held long enough).</summary>
    public event Action<PowerAction>? Run;

    public PowerTab()
    {
        InitializeComponent();
        foreach (var item in Items) Buttons.Children.Add(Build(item));
        Refresh();
    }

    /// <summary>The tab came into view.</summary>
    public void Refresh()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var span = up.TotalDays >= 1 ? $"{(int)up.TotalDays} d {up.Hours} h" : up.TotalHours >= 1 ? $"{(int)up.TotalHours} h {up.Minutes} min" : $"{up.Minutes} min";
        StatusText.Foreground = Res("MutedBrush");
        StatusText.Text = $"Your PC has been on for {span}.";
    }

    public void ShowStatus(string text, bool problem)
    {
        StatusText.Foreground = Res(problem ? "TextBrush" : "MutedBrush");
        StatusText.Text = text;
    }

    private FrameworkElement Build(Item item)
    {
        var face = new Border
        {
            Width = 80, Height = 80, CornerRadius = new CornerRadius(40), Background = Res("PanelFillBrush"),
            Child = new LineIcon { Icon = item.Icon, Width = 32, Height = 32 },
        };
        var ring = new Ring
        {
            Width = 94, Height = 94, StrokeWidth = 4, Progress = 0, TrackBrush = Brushes.Transparent,
            RingBrush = item.HoldBrush is null ? Brushes.Transparent : Res(item.HoldBrush),
        };
        var focus = new Border // keyboard focus: a thin grey ring
        {
            Width = 94, Height = 94, CornerRadius = new CornerRadius(47), BorderThickness = new Thickness(1.5),
            BorderBrush = Res("MutedBrush"), Visibility = Visibility.Hidden,
        };
        var circle = new Grid { Width = 94, Height = 94, HorizontalAlignment = HorizontalAlignment.Center };
        circle.Children.Add(ring);
        circle.Children.Add(face);
        circle.Children.Add(focus);

        var content = new StackPanel();
        content.Children.Add(circle);
        content.Children.Add(new TextBlock { Text = item.Label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        content.Children.Add(new TextBlock { Text = item.Hint, FontSize = 11.5, Foreground = Res("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });

        var button = new Button { Style = (Style)FindResource("Plain"), Content = content, ToolTip = item.Hint };
        System.Windows.Automation.AutomationProperties.SetName(button, item.Label);
        button.MouseEnter += (_, _) => face.Background = Res("FillHoverBrush");
        button.MouseLeave += (_, _) => face.Background = Res("PanelFillBrush");
        button.GotKeyboardFocus += (_, _) => focus.Visibility = Visibility.Visible;
        button.LostKeyboardFocus += (_, _) => { focus.Visibility = Visibility.Hidden; Cancel(item, ring); };

        if (item.HoldBrush is null)
        {
            button.Click += (_, _) => Run?.Invoke(item.Action);
            return button;
        }

        // Hold to confirm. The button captures the mouse while pressed, so the notch can't close mid-hold.
        button.PreviewMouseLeftButtonDown += (_, _) => StartHold(item, ring);
        button.PreviewMouseLeftButtonUp += (_, _) => Cancel(item, ring);
        button.LostMouseCapture += (_, _) => Cancel(item, ring);
        button.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true; // never a plain click
            if (!e.IsRepeat) StartHold(item, ring);
        };
        button.PreviewKeyUp += (_, e) => { if (e.Key is Key.Enter or Key.Space) Cancel(item, ring); };
        return button;
    }

    private void StartHold(Item item, Ring ring)
    {
        _holding = item.Action;
        ShowStatus($"Keep holding to {item.Label.ToLowerInvariant()}…", problem: false);
        var fill = new DoubleAnimation(0, 1, HoldTime);
        fill.Completed += (_, _) =>
        {
            if (_holding != item.Action) return; // let go before the ring closed
            _holding = null;
            ring.BeginAnimation(Ring.ProgressProperty, null);
            ring.Progress = 0;
            Run?.Invoke(item.Action);
        };
        ring.BeginAnimation(Ring.ProgressProperty, fill);
    }

    private void Cancel(Item item, Ring ring)
    {
        if (_holding != item.Action) return;
        _holding = null;
        ring.BeginAnimation(Ring.ProgressProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        ShowStatus($"Let go too early, so nothing happened. Hold until the ring closes.", problem: false);
    }

    private Brush Res(string key) => (Brush)FindResource(key);
}
