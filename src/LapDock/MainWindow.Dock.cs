using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LapDock.Services;

namespace LapDock;

/// <summary>Pinned apps: real shell icons, magnify, launch bounce, drag-to-pin, right-click to unpin.</summary>
public partial class MainWindow
{
    private const double DockIconSize = 52;
    private const double DockGap = 6; // per side → 12 px between icons

    /// <summary>What can be pinned by dropping it on the notch.</summary>
    private static readonly string[] PinnableExtensions = { ".exe", ".lnk", ".url", ".appref-ms" };

    private readonly List<Button> _dockButtons = new();

    private void BuildDock()
    {
        DockPanel.Children.Clear();
        _dockButtons.Clear();
        foreach (var pin in _settings.PinnedApps)
        {
            var parts = pin.Split('|', 2);
            if (parts.Length != 2) continue;
            var (label, target) = (parts[0], parts[1]);
            var (icon, shellName) = ShellIconService.Get(target);

            UIElement face;
            if (icon is not null)
            {
                var img = new Image { Source = icon, Width = DockIconSize, Height = DockIconSize };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                face = img;
            }
            else face = FallbackTile(label);

            var btn = new Button
            {
                Style = (Style)FindResource("DockIcon"),
                Content = face,
                Width = DockIconSize,
                Height = DockIconSize,
                Margin = new Thickness(DockGap, 0, DockGap, 0),
                // Your label when it is a real name ("File Explorer"), the shell's for old emoji labels.
                ToolTip = label.Length > 2 ? label : shellName ?? label,
                RenderTransformOrigin = new Point(0.5, 0.5), // grows in place, stays inside the pill
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } },
                ContextMenu = PinMenu(pin),
            };
            ToolTipService.SetPlacement(btn, PlacementMode.Bottom);
            ToolTipService.SetVerticalOffset(btn, 14);
            btn.Click += (_, _) => { Bounce(btn); Launch(btn, target); };
            btn.MouseEnter += (_, _) => Magnify(btn);
            btn.MouseLeave += (_, _) => Magnify(null);
            _dockButtons.Add(btn);
            DockPanel.Children.Add(btn);
        }

        // The expanded notch is at least wide enough for every pinned icon.
        DockPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _expandedWidth = Math.Max(ExpandedMinWidth, DockPanel.DesiredSize.Width + 2 * DockIconSize);
        Expanded.Width = _expandedWidth;
        Width = _expandedWidth + 40;
        if (_mode == Mode.Expanded) ResizePill();
        if (IsLoaded) PlaceOnScreen();
    }

    /// <summary>iOS-style squircle with the pin's label, for apps the shell has no icon for.</summary>
    private static Border FallbackTile(string label) => new()
    {
        Width = DockIconSize,
        Height = DockIconSize,
        CornerRadius = new CornerRadius(DockIconSize * 0.225),
        Background = new LinearGradientBrush(Color.FromRgb(0x48, 0x48, 0x4A), Color.FromRgb(0x1C, 0x1C, 0x1E), 90),
        Child = new TextBlock
        {
            Text = label.Length <= 2 ? label : label[..1].ToUpperInvariant(), // emoji labels from older settings stay as-is
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }
    };

    private ContextMenu PinMenu(string pin)
    {
        var remove = new MenuItem { Header = "Remove from dock" };
        remove.Click += (_, _) =>
        {
            _settings.PinnedApps.Remove(pin);
            SettingsService.Save(_settings);
            BuildDock();
        };
        return new ContextMenu { Items = { remove } };
    }

    /// <summary>Hovered icon grows, neighbours a touch — sized so they never overlap or leave the pill.</summary>
    private void Magnify(Button? hovered)
    {
        int h = hovered is null ? -1 : _dockButtons.IndexOf(hovered);
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < _dockButtons.Count; i++)
        {
            int d = h < 0 ? int.MaxValue : Math.Abs(i - h);
            double s = d switch { 0 => 1.3, 1 => 1.1, _ => 1.0 };
            var scale = (ScaleTransform)((TransformGroup)_dockButtons[i].RenderTransform).Children[0];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s, duration) { EasingFunction = ease });
        }
    }

    /// <summary>Launch feedback: two small hops downward, away from the screen edge.</summary>
    private static void Bounce(Button b)
    {
        var shift = (TranslateTransform)((TransformGroup)b.RenderTransform).Children[1];
        var up = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var down = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var hop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(10, KeyTime.FromPercent(0.22), up));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.45), down));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(5, KeyTime.FromPercent(0.65), up));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.85), down));
        shift.BeginAnimation(TranslateTransform.YProperty, hop);
    }

    /// <summary>"Can't open that" — the iOS wrong-passcode shake.</summary>
    private static void Shake(Button b)
    {
        var shift = (TranslateTransform)((TransformGroup)b.RenderTransform).Children[1];
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(400) };
        foreach (var (x, t) in new[] { (-6.0, 0.15), (6.0, 0.35), (-4.0, 0.55), (4.0, 0.75), (0.0, 1.0) })
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromPercent(t)));
        shift.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    private static void Launch(Button b, string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(target)) { UseShellExecute = true });
        }
        catch
        {
            Shake(b);
            System.Media.SystemSounds.Beep.Play();
        }
    }

    // ------------------------------------------------------------- drag & drop
    private static string[] PinnablePaths(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(p => PinnableExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToArray()
            : Array.Empty<string>();

    private void Island_DragOver(object sender, DragEventArgs e)
    {
        bool pinnable = PinnablePaths(e).Length > 0;
        e.Effects = pinnable ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
        if (!pinnable) return;
        _dragInside = true;
        _collapseTimer.Stop();
        if (DropSlot.Visibility != Visibility.Visible)
        {
            DropSlot.Visibility = Visibility.Visible;
            Pill.BorderThickness = new Thickness(1.5);
            if (_mode == Mode.Expanded) ResizePill();
        }
        SetMode(Mode.Expanded);
    }

    private void Island_DragLeave(object sender, DragEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _dragInside = false;
        DropSlot.Visibility = Visibility.Collapsed;
        Pill.BorderThickness = new Thickness(0);
        RestartCollapse();
    }

    /// <summary>Drop apps/shortcuts on the notch to pin them (duplicates are ignored).</summary>
    private void Island_Drop(object sender, DragEventArgs e)
    {
        var added = PinnablePaths(e)
            .Where(p => !_settings.PinnedApps.Any(pin => pin.EndsWith("|" + p, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        EndDrag();
        if (added.Count == 0) return;
        foreach (var p in added)
        {
            // Label = the shell's name for it ("Discord", not "Update.exe"); the icon resolves from the path.
            var name = ShellIconService.Get(p).Name ?? Path.GetFileNameWithoutExtension(p);
            _settings.PinnedApps.Add($"{name}|{p}");
        }
        SettingsService.Save(_settings);
        BuildDock();
    }
}
