using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LapDock.Controls;
using LapDock.Models;
using LapDock.Services;

namespace LapDock.Views.Tabs;

/// <summary>
/// Stats: CPU / memory / GPU rings, top apps by memory, live internet throughput, ping and a
/// download speed test. Everything samples once a second, and only while the tab is on screen
/// (see <see cref="Active"/>) — nothing runs while the notch is closed.
/// </summary>
public partial class StatsTab : UserControl, IDisposable
{
    private static readonly Duration Ease = TimeSpan.FromMilliseconds(450);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SystemStatsService _system = new();
    private readonly NetworkService _network = new();
    private ProcessMemoryService? _apps; // opened on first view: the process counter is the heaviest read here
    private string _speedUrl = new AppSettings().SpeedTestUrl;
    private bool _active, _testing;
    private int _tick;

    public StatsTab()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Sample();
        CpuDetail.Text = $"{Environment.ProcessorCount} threads";
    }

    public void Attach(AppSettings settings) => _speedUrl = settings.SpeedTestUrl;

    /// <summary>The notch is open on this tab: sample every second. Otherwise stop.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            _active = value;
            if (!value) { _timer.Stop(); return; }
            _apps ??= new ProcessMemoryService();
            _tick = 0;
            Sample();
            _timer.Start();
        }
    }

    private void Sample()
    {
        var s = _system.GetStats();
        SetGauge(CpuRing, CpuValue, s.CpuPercent);
        SetGauge(RamRing, RamValue, s.RamPercent);
        RamDetail.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB";

        bool gpu = _system.HasGpu;
        GpuCard.Visibility = gpu ? Visibility.Visible : Visibility.Collapsed;
        Gauges.Columns = gpu ? 3 : 2;
        if (gpu && _system.GetGpuPercent() is { } load) SetGauge(GpuRing, GpuValue, load);

        var (down, up) = _network.Throughput();
        DownRate.Text = NetworkService.FormatRate(down);
        UpRate.Text = NetworkService.FormatRate(up);

        if (_tick % 2 == 0) RenderApps();
        if (_tick % 5 == 0) _ = UpdatePingAsync();
        _tick++;
    }

    /// <summary>Shows the number and eases the ring to it.</summary>
    private void SetGauge(Ring ring, Run value, double percent)
    {
        value.Text = $"{percent:0}";
        ring.BeginAnimation(Ring.ProgressProperty,
            new DoubleAnimation(percent / 100, Ease) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void RenderApps()
    {
        var top = _apps?.Top(5) ?? new();
        AppList.Children.Clear();
        if (top.Count == 0)
        {
            AppList.Children.Add(new TextBlock { Text = "Memory per app isn't available on this PC.", FontSize = 12, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        long max = top.Max(a => a.Bytes);
        foreach (var app in top)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(app.Icon is { } icon
                ? Smooth(new Image { Source = icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center })
                : new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = Res("FillHoverBrush"), HorizontalAlignment = HorizontalAlignment.Left });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = app.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = app.Name });
            var bar = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 0) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)) });
            bar.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(1.5), Background = Res("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0, Tag = (double)app.Bytes / max,
            });
            bar.SizeChanged += (_, e) => { var fill = (Border)bar.Children[1]; fill.Width = e.NewSize.Width * (double)fill.Tag; };
            text.Children.Add(bar);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var size = new TextBlock { Text = FormatBytes(app.Bytes), FontSize = 12, Foreground = Res("MutedBrush"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 2);
            row.Children.Add(size);
            AppList.Children.Add(row);
        }
    }

    private static Image Smooth(Image image)
    {
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>Task Manager's units: 1 GB = 1024 MB.</summary>
    private static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1L << 20):0} MB";

    private async Task UpdatePingAsync()
    {
        var ms = await NetworkService.PingAsync(Host());
        PingText.Text = ms is { } m ? $"Ping {m} ms" : "Ping –";
    }

    private string Host() => Uri.TryCreate(_speedUrl, UriKind.Absolute, out var uri) ? uri.Host : "1.1.1.1";

    private async void TestSpeed_Click(object sender, RoutedEventArgs e)
    {
        if (_testing) return;
        _testing = true;
        SpeedButton.IsEnabled = false;
        SpeedValue.Visibility = Visibility.Collapsed;
        SpeedStatus.Text = "Finding a server…";
        try
        {
            double mbps = await NetworkService.DownloadMbpsAsync(_speedUrl,
                firstByte: () => SpeedStatus.Text = "Testing download…",
                progress: live => ShowSpeed(live, "Testing download…"));
            ShowSpeed(mbps, $"Mbps download, tested {DateTime.Now:t}");
            SpeedButton.Content = "Test again";
        }
        catch (Exception)
        {
            SpeedValue.Visibility = Visibility.Collapsed;
            SpeedStatus.Text = "Couldn't reach the test server. Check your connection and try again.";
        }
        finally
        {
            _testing = false;
            SpeedButton.IsEnabled = true;
        }
    }

    private void ShowSpeed(double mbps, string status)
    {
        SpeedValue.Text = mbps >= 100 ? $"{mbps:0}" : $"{mbps:0.0}";
        SpeedValue.Visibility = Visibility.Visible;
        SpeedStatus.Text = status;
    }

    private void TaskManager_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
        catch (Exception) { /* blocked by policy: nothing to open */ }
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    public void Dispose()
    {
        _timer.Stop();
        _apps?.Dispose();
        _system.Dispose();
    }
}
