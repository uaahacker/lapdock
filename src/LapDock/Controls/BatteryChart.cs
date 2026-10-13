using System.Windows;
using System.Windows.Media;

namespace LapDock.Controls;

/// <summary>
/// The battery panel's last 24 hours, like the iPhone's: one bar per half hour at the charge level it ended on,
/// green while plugged in. Gaps are times the PC was off or asleep.
/// </summary>
public sealed class BatteryChart : FrameworkElement
{
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), 1));
    private (int Percent, bool PluggedIn)?[] _slots = Array.Empty<(int, bool)?>();

    public (int Percent, bool PluggedIn)?[] Slots
    {
        get => _slots;
        set { _slots = value; InvalidateVisual(); }
    }

    public Brush BarBrush { get; set; } = Brushes.Gray;
    public Brush PluggedBrush { get; set; } = Brushes.Green;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        foreach (var y in new[] { 0.5, Math.Round(h / 2) + .5, h - .5 }) // 100%, 50%, 0%
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        if (_slots.Length == 0) return;
        double step = w / _slots.Length, bar = Math.Max(2, step - 3);
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is not { } s) continue;
            double height = Math.Max(2, (h - 1) * s.Percent / 100.0);
            dc.DrawRoundedRectangle(s.PluggedIn ? PluggedBrush : BarBrush, null,
                new Rect(i * step + (step - bar) / 2, h - 1 - height, bar, height), 1.5, 1.5);
        }
    }

    private static Pen Freeze(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
