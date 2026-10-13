using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LapDock.Controls;

/// <summary>
/// A progress ring (track + arc from 12 o'clock, clockwise) with any content centred inside.
/// The Today tab's timer uses it; Phase 4's CPU/RAM gauges are the same control.
/// </summary>
public sealed class Ring : ContentControl
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(nameof(RingBrush), typeof(Brush), typeof(Ring),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(Ring),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0..1</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Brush RingBrush { get => (Brush)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    // ContentControl's theme template ignores *ContentAlignment: use one that centres the content.
    // Set per instance (a local value), because the theme style would override a metadata default.
    private static readonly ControlTemplate Centered = CreateCentered();

    public Ring() => Template = Centered;

    private static ControlTemplate CreateCentered()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var template = new ControlTemplate(typeof(Ring)) { VisualTree = presenter };
        template.Seal();
        return template;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double t = StrokeWidth, size = Math.Min(ActualWidth, ActualHeight);
        if (size <= t) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = (size - t) / 2;
        dc.DrawEllipse(null, new Pen(TrackBrush, t), c, r, r);

        double p = Math.Clamp(Progress, 0, 1);
        if (p <= 0) return;
        var pen = new Pen(RingBrush, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (p >= 0.9999) { dc.DrawEllipse(null, pen, c, r, r); return; } // an arc can't close on itself

        double a = p * 2 * Math.PI;
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), false, false);
            g.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        dc.DrawGeometry(null, pen, arc);
    }
}
