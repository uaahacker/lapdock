using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LapDock.Models;
using LapDock.Services;
using Microsoft.Win32;

namespace LapDock.Views.Tabs;

/// <summary>One picture in the grid (or the "Add folder" tile). Its thumbnail loads the first time it's shown.</summary>
public sealed class WallpaperThumb : INotifyPropertyChanged
{
    private ImageSource? _image;
    private bool _requested, _current;

    public string Path { get; init; } = "";
    public bool IsAdd { get; init; }
    public string Name => IsAdd ? "Add a folder of pictures" : System.IO.Path.GetFileName(Path);
    public bool IsCurrent { get => _current; set { _current = value; Notify(); } }

    public ImageSource? Image
    {
        get
        {
            if (!_requested && !IsAdd) { _requested = true; _ = LoadAsync(); }
            return _image;
        }
    }

    private async Task LoadAsync()
    {
        _image = await WallpaperService.Thumbnail(Path, 256);
        Notify(nameof(Image));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record WallpaperRow(IReadOnlyList<WallpaperThumb> Items);

/// <summary>
/// Wallpaper: the current desktop picture large, its fit, and a grid of pictures — click one and the
/// desktop changes at once. Pictures come from Pictures, Pictures\Wallpapers, Windows' own wallpapers,
/// recent ones and folders added here.
/// </summary>
public partial class WallpaperTab : UserControl
{
    private static readonly WallpaperStyle[] FitStyles =
        { WallpaperStyle.Fill, WallpaperStyle.Fit, WallpaperStyle.Stretch, WallpaperStyle.Tile, WallpaperStyle.Center, WallpaperStyle.Span };

    private AppSettings _settings = new();
    private Func<Func<bool?>, bool?>? _modal;
    private List<WallpaperThumb> _thumbs = new();
    private DateTime _scannedAt = DateTime.MinValue;
    private bool _scanning;
    private string? _current;

    public WallpaperTab()
    {
        InitializeComponent();
        foreach (var style in FitStyles)
        {
            var pill = new Button { Content = style.ToString(), Style = (Style)FindResource("FitPill"), ToolTip = FitHelp(style) };
            pill.Click += async (_, _) => await SetFitAsync(style);
            Fits.Children.Add(pill);
        }
    }

    /// <param name="modal">Runs a dialog without the notch collapsing underneath it.</param>
    public void Attach(AppSettings settings, Func<Func<bool?>, bool?> modal)
    {
        _settings = settings;
        _modal = modal;
    }

    /// <summary>The tab came into view.</summary>
    public void Refresh()
    {
        RefreshCurrent();
        if (DateTime.Now - _scannedAt > TimeSpan.FromMinutes(2)) _ = ScanAsync();
    }

    /// <summary>Re-reads what the desktop shows (also called when it changes elsewhere, e.g. in Settings).</summary>
    public async void RefreshCurrent()
    {
        _current = WallpaperService.Current();
        CurrentName.Text = _current is null ? "No picture wallpaper" : Path.GetFileNameWithoutExtension(_current);
        CurrentFolder.Text = _current is null ? "A solid colour or Windows Spotlight is on. Pick a picture below."
            : $"in {Path.GetFileName(Path.GetDirectoryName(_current))}";
        CurrentFolder.ToolTip = _current;
        var style = WallpaperService.CurrentStyle()?.ToString();
        foreach (Button pill in Fits.Children) pill.Tag = (string)pill.Content == style ? "on" : null;
        foreach (var t in _thumbs) t.IsCurrent = t.Path.Equals(_current, StringComparison.OrdinalIgnoreCase);
        var shown = _current;
        var preview = _current is null ? null : await WallpaperService.Thumbnail(_current, 900);
        if (shown == _current) PreviewBrush.ImageSource = preview; // a newer pick may have landed meanwhile
    }

    private async Task ScanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var added = _settings.WallpaperFolders.ToList();
            var recent = _settings.RecentWallpapers.ToList();
            var current = _current;
            var pictures = await Task.Run(() => WallpaperService.FindPictures(added, recent, current));
            _thumbs = pictures.Select(p => new WallpaperThumb { Path = p, IsCurrent = p.Equals(_current, StringComparison.OrdinalIgnoreCase) }).ToList();
            var tiles = _thumbs.Append(new WallpaperThumb { IsAdd = true }).ToList();
            ThumbList.ItemsSource = tiles.Chunk(4).Select(row => new WallpaperRow(row)).ToList();
            CountText.Text = pictures.Count == 1 ? "1 picture" : $"{pictures.Count} pictures";
            _scannedAt = DateTime.Now;
        }
        finally { _scanning = false; }
    }

    private async void Thumb_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WallpaperThumb thumb) return;
        if (thumb.IsAdd) await AddFolderAsync();
        else await ApplyAsync(thumb);
    }

    private async Task ApplyAsync(WallpaperThumb thumb)
    {
        StatusText.Visibility = Visibility.Collapsed;
        // Instant feedback: the preview and the ring move now; the shell takes a moment to transcode.
        if (thumb.Image is { } quick) PreviewBrush.ImageSource = quick;
        foreach (var t in _thumbs) t.IsCurrent = t == thumb;
        CurrentName.Text = Path.GetFileNameWithoutExtension(thumb.Path);
        try
        {
            await WallpaperService.Apply(thumb.Path, WallpaperService.CurrentStyle() ?? WallpaperStyle.Fill);
            _settings.RecentWallpapers.RemoveAll(p => p.Equals(thumb.Path, StringComparison.OrdinalIgnoreCase));
            _settings.RecentWallpapers.Insert(0, thumb.Path);
            if (_settings.RecentWallpapers.Count > 10) _settings.RecentWallpapers.RemoveRange(10, _settings.RecentWallpapers.Count - 10);
            SettingsService.Save(_settings);
        }
        catch (Exception ex) { ShowStatus($"Couldn't set {thumb.Name} as the wallpaper: {ex.Message}"); }
        RefreshCurrent();
    }

    private async Task SetFitAsync(WallpaperStyle style)
    {
        try { await WallpaperService.SetStyle(style); }
        catch (Exception ex) { ShowStatus($"Couldn't change the fit: {ex.Message}"); }
        RefreshCurrent();
    }

    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder of wallpapers" };
        bool? ok = _modal is null ? dialog.ShowDialog() : _modal(() => dialog.ShowDialog(Window.GetWindow(this)));
        if (ok != true) return;
        if (!_settings.WallpaperFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            _settings.WallpaperFolders.Add(dialog.FolderName);
            SettingsService.Save(_settings);
        }
        _scannedAt = DateTime.MinValue;
        await ScanAsync();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUri("ms-settings:personalization-background");

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private static string FitHelp(WallpaperStyle style) => style switch
    {
        WallpaperStyle.Fill => "Fill the screen, cropping the edges",
        WallpaperStyle.Fit => "Show the whole picture, with bars if needed",
        WallpaperStyle.Stretch => "Stretch to the screen's shape",
        WallpaperStyle.Tile => "Repeat the picture",
        WallpaperStyle.Center => "Original size, centred",
        _ => "One picture across all displays",
    };
}
