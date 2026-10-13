using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LapDock.Models;
using LapDock.Services;

namespace LapDock.Views.Tabs;

public enum LaunchKind { Header, App, File, Snippet }

/// <summary>One row of the launcher list: a section header, an app, a file or a snippet.</summary>
public sealed class LaunchItem : INotifyPropertyChanged
{
    private bool _favourite;
    private ImageSource? _icon;
    private bool _iconLoaded;

    public LaunchKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    /// <summary>App: shell:AppsFolder\id. File: full path.</summary>
    public string? Target { get; init; }
    public Snippet? Snippet { get; init; }

    public bool IsHeader => Kind == LaunchKind.Header;
    public bool IsEntry => Kind != LaunchKind.Header;
    public bool IsApp => Kind == LaunchKind.App;
    public bool IsSnippet => Kind == LaunchKind.Snippet;
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public string KindLabel => Kind switch { LaunchKind.File => "File", LaunchKind.Snippet => "Snippet", _ => "" };
    /// <summary>Placeholder until the real icon loads: LapDock's icon, or a Segoe glyph for files (the pack has none).</summary>
    public string? IconName => Kind == LaunchKind.Snippet ? "clipboard" : Kind == LaunchKind.App ? "apps" : null;
    public string Glyph => Kind == LaunchKind.File ? "" : "";
    public bool IsFavourite { get => _favourite; set { _favourite = value; Notify(); } }

    /// <summary>Asked from the shell when the row first scrolls into view (the list is virtualised).</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded && Target is not null && Kind is LaunchKind.App or LaunchKind.File)
                _icon = ShellIconService.Get(Target, 64).Icon; // 64 px, scaled down smoothly: crisp at 125–150% too
            _iconLoaded = true;
            return _icon;
        }
    }

    public bool HasIcon => Icon is not null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Apps: a launcher over every Start app, files in the index folders (Documents and Desktop by
/// default) and snippets. Favourites stay on top. Enter opens; a snippet is pasted into the app you were in.
/// </summary>
public partial class AppsTab : UserControl
{
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(10);

    private readonly ObservableCollection<LaunchItem> _items = new();
    private List<AppEntry> _apps = AppCatalogService.LoadCache();
    private readonly HashSet<string> _favourites = AppCatalogService.LoadFavourites();
    private List<string> _folders = DefaultFolders();
    private List<string>? _files;
    private DateTime _appsAt = DateTime.MinValue, _filesAt = DateTime.MinValue;
    private bool _refreshing, _indexing, _favouritesOnly;

    public event Action? Launched;
    /// <summary>A snippet was chosen: paste this text into the previous app.</summary>
    public event Action<string>? PasteRequested;
    public event Action? CloseRequested;

    public AppsTab()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        Rebuild();
    }

    public void Attach(AppSettings settings)
    {
        if (settings.LauncherFolders.Count > 0) _folders = settings.LauncherFolders;
    }

    private static List<string> DefaultFolders() => new()
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
    };

    /// <summary>The tab came into view: show the cached list now, refresh apps in the background if stale.</summary>
    public void Refresh()
    {
        Rebuild();
        if (DateTime.Now - _appsAt > Stale) _ = RefreshAppsAsync();
    }

    public void FocusSearch()
    {
        SearchBox.SelectAll();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private async Task RefreshAppsAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            _apps = await AppCatalogService.RefreshAsync();
            _appsAt = DateTime.Now;
            Rebuild();
        }
        catch (Exception) { /* keep the cached list */ }
        finally { _refreshing = false; }
    }

    private async void EnsureFiles()
    {
        if (_indexing || (_files is not null && DateTime.Now - _filesAt < Stale)) return;
        _indexing = true;
        var folders = _folders.ToList();
        _files = await Task.Run(() => AppCatalogService.IndexFiles(folders));
        _filesAt = DateTime.Now;
        _indexing = false;
        if (SearchBox.Text.Trim().Length > 0) Rebuild();
    }

    // ------------------------------------------------------------------ list
    private void Rebuild()
    {
        var keep = (List.SelectedItem as LaunchItem)?.Target;
        var q = SearchBox.Text.Trim();
        _items.Clear();
        if (q.Length == 0) BuildHome();
        else BuildResults(q);

        Empty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Text = q.Length > 0 ? $"Nothing called \"{q}\" in your apps, files or snippets."
            : _favouritesOnly ? "No favourites yet. Star an app to keep it here."
            : "Finding your apps…";
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        FavouritesOnly.Foreground = (Brush)FindResource(_favouritesOnly ? "AccentBrush" : "TextBrush");

        var reselect = _items.FirstOrDefault(i => i.IsEntry && keep is not null && i.Target == keep) ?? _items.FirstOrDefault(i => i.IsEntry);
        List.SelectedItem = reselect;
        if (reselect is not null) List.ScrollIntoView(reselect);
    }

    private void BuildHome()
    {
        var favourites = _apps.Where(a => _favourites.Contains(a.Target)).ToList();
        Section("Favourites", favourites.Select(AppItem));
        if (_favouritesOnly) return;
        Section("Snippets", AppCatalogService.LoadSnippets().Select(SnippetItem));
        Section("All apps", _apps.Where(a => !_favourites.Contains(a.Target)).Select(AppItem));
    }

    private void BuildResults(string q)
    {
        var apps = _apps
            .Select(a => (App: a, Score: AppCatalogService.Score(a.Name, q)))
            .Where(x => x.Score > 0 && (!_favouritesOnly || _favourites.Contains(x.App.Target)))
            .OrderByDescending(x => x.Score).ThenByDescending(x => _favourites.Contains(x.App.Target))
            .ThenBy(x => x.App.Name.Length).ThenBy(x => x.App.Name)
            .Take(12).Select(x => AppItem(x.App));
        Section("Apps", apps);
        if (_favouritesOnly) return;

        var snippets = AppCatalogService.LoadSnippets()
            .Where(s => s.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .Take(5).Select(SnippetItem);
        Section("Snippets", snippets);

        EnsureFiles();
        var files = (_files ?? new())
            .Select(path => (Path: path, At: Path.GetFileName(path).IndexOf(q, StringComparison.CurrentCultureIgnoreCase)))
            .Where(x => x.At >= 0)
            .OrderBy(x => x.At == 0 ? 0 : 1).ThenByDescending(x => SafeWriteTime(x.Path))
            .Take(8).Select(x => FileItem(x.Path));
        Section("Files", files);
    }

    private void Section(string title, IEnumerable<LaunchItem> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return;
        _items.Add(new LaunchItem { Kind = LaunchKind.Header, Title = title });
        foreach (var row in list) _items.Add(row);
    }

    private LaunchItem AppItem(AppEntry app) =>
        new() { Kind = LaunchKind.App, Title = app.Name, Target = app.Target, IsFavourite = _favourites.Contains(app.Target) };

    private static LaunchItem SnippetItem(Snippet s) => new()
    {
        Kind = LaunchKind.Snippet, Title = s.Name, Snippet = s, Target = "snippet:" + s.Name,
        Subtitle = s.Text.Replace("\r", " ").Replace("\n", " "),
    };

    private LaunchItem FileItem(string path)
    {
        var root = _folders.FirstOrDefault(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase));
        var folder = Path.GetDirectoryName(path) ?? "";
        var where = root is null ? folder : Path.Combine(Path.GetFileName(root), Path.GetRelativePath(root, folder)).TrimEnd('.', '\\');
        return new LaunchItem { Kind = LaunchKind.File, Title = Path.GetFileName(path), Subtitle = where, Target = path };
    }

    private static DateTime SafeWriteTime(string path)
    {
        try { return File.GetLastWriteTime(path); } catch (Exception) { return DateTime.MinValue; }
    }

    // ----------------------------------------------------------------- input
    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter: Open(List.SelectedItem as LaunchItem); e.Handled = true; break;
            case Key.Escape:
                if (SearchBox.Text.Length > 0) SearchBox.Clear();
                else CloseRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Moves the selection, skipping section headers.</summary>
    private void Move(int delta)
    {
        int i = List.SelectedIndex;
        do i += delta; while (i >= 0 && i < _items.Count && _items[i].IsHeader);
        if (i < 0 || i >= _items.Count) return;
        List.SelectedIndex = i;
        List.ScrollIntoView(List.SelectedItem);
    }

    private void List_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(List, d) is ListBoxItem { DataContext: LaunchItem item })
            Open(item);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Open(List.SelectedItem as LaunchItem);
        else if (e.Key == Key.Escape) CloseRequested?.Invoke();
    }

    private void Open(LaunchItem? item)
    {
        if (item is null || item.IsHeader) return;
        if (item.Kind == LaunchKind.Snippet && item.Snippet is { } snippet)
        {
            PasteRequested?.Invoke(snippet.Text);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(item.Target!) { UseShellExecute = true });
            SearchBox.Clear();
            Launched?.Invoke();
        }
        catch (Exception) { StatusText.Text = $"Couldn't open {item.Title}. It may have been uninstalled or moved."; }
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LaunchItem { Target: { } target } item) return;
        if (!_favourites.Add(target)) _favourites.Remove(target);
        item.IsFavourite = _favourites.Contains(target);
        AppCatalogService.SaveFavourites(_favourites);
        e.Handled = true;
        Rebuild();
    }

    private void RemoveSnippet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LaunchItem { Snippet: { } snippet }) return;
        AppCatalogService.RemoveSnippet(snippet);
        e.Handled = true;
        Rebuild();
    }

    private void FavouritesOnly_Click(object sender, RoutedEventArgs e)
    {
        _favouritesOnly = !_favouritesOnly;
        Rebuild();
    }
}
