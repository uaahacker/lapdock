using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LapDock.Controls;

/// <summary>
/// One of LapDock's own icons (assets/icons, generated into LineIcons.cs): 48×48 line drawings drawn
/// as vectors in the current text colour, so they stay sharp at any scaling and recolour like text
/// (green when on, black on a green circle, grey when idle).
/// <code>&lt;controls:LineIcon Icon="wifi" Width="18" Height="18"/&gt;</code>
/// </summary>
public sealed partial class LineIcon : FrameworkElement
{
    private static readonly Dictionary<string, (Geometry? Stroke, Geometry? Fill)> Parsed = new();

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(LineIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Inherited like a TextBlock's, so an icon inside a button takes the button's colour.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(LineIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public static bool Exists(string name) => Paths.ContainsKey(name);

    protected override Size MeasureOverride(Size available) => new(16, 16); // Width/Height, when set, win

    protected override void OnRender(DrawingContext dc)
    {
        if (Icon is null || Shape(Icon) is not var (stroke, fill)) return;
        double scale = Math.Min(ActualWidth, ActualHeight) / 48;
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 48 * scale) / 2, (ActualHeight - 48 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        // The pack's 3/48 stroke, but never thinner than ~1.5 px: tiny icons stay readable.
        var pen = new Pen(Foreground, Math.Max(3, 1.5 / scale))
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        if (stroke is not null) dc.DrawGeometry(null, pen, stroke);
        if (fill is not null) dc.DrawGeometry(Foreground, null, fill);
        dc.Pop();
        dc.Pop();
    }

    private static (Geometry? Stroke, Geometry? Fill)? Shape(string name)
    {
        if (Parsed.TryGetValue(name, out var hit)) return hit;
        if (!Paths.TryGetValue(name, out var data)) return null;
        return Parsed[name] = (Parse(data.Stroke), Parse(data.Fill));
    }

    private static Geometry? Parse(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
