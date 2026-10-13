using System.Windows;
using System.Windows.Media;

namespace LapDock.Controls;

/// <summary>
/// The notch's music bars: five rounded bars, centred like a waveform, drawn straight from <see cref="Levels"/>
/// (0..1). Redrawing them never triggers a layout pass — they move ~15 times a second while music plays.
/// </summary>
public sealed class MusicBars : FrameworkElement
{
    private const int Count = 5;
    private const double BarWidth = 3, Gap = 2.5, MinHeight_ = 3, MaxHeight_ = 16;

    public double[] Levels { get; } = new double[Count];
    public Brush Fill { get; set; } = Brushes.Green;

    protected override Size MeasureOverride(Size available) => new(Count * BarWidth + (Count - 1) * Gap, MaxHeight_);

    protected override void OnRender(DrawingContext dc)
    {
        for (int i = 0; i < Count; i++)
        {
            double h = MinHeight_ + (MaxHeight_ - MinHeight_) * Math.Clamp(Levels[i], 0, 1);
            dc.DrawRoundedRectangle(Fill, null, new Rect(i * (BarWidth + Gap), (MaxHeight_ - h) / 2, BarWidth, h), BarWidth / 2, BarWidth / 2);
        }
    }
}
