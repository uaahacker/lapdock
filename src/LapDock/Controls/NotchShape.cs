using System.Windows;
using System.Windows.Media;

namespace LapDock.Controls;

/// <summary>
/// The notch's black shape. Flush with the top of the screen, with small concave "shoulders" where it
/// meets the edge (so it reads as part of the bezel, like a MacBook notch, instead of a box with square
/// top corners) and round bottom corners that soften as it grows. Width and Height are animated; the
/// shape is rebuilt every frame. The shoulders reach <see cref="Shoulder"/> px beyond each side.
/// </summary>
public sealed class NotchShape : FrameworkElement
{
    public const double Shoulder = 9;
    private const double MaxCorner = 26;

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double r = Math.Min(Math.Min(h / 2, w / 2), MaxCorner); // a pill when small, a soft card when open
        double e = Math.Min(Shoulder, h / 2);

        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(-e, 0), isFilled: true, isClosed: true);
            g.ArcTo(new Point(0, e), new Size(e, e), 0, false, SweepDirection.Clockwise, true, true);         // left shoulder (concave)
            g.LineTo(new Point(0, h - r), true, true);
            g.ArcTo(new Point(r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);  // bottom-left
            g.LineTo(new Point(w - r, h), true, true);
            g.ArcTo(new Point(w, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true); // bottom-right
            g.LineTo(new Point(w, e), true, true);
            g.ArcTo(new Point(w + e, 0), new Size(e, e), 0, false, SweepDirection.Clockwise, true, true);      // right shoulder
        }
        shape.Freeze();
        dc.DrawGeometry(Brushes.Black, null, shape);
    }
}
