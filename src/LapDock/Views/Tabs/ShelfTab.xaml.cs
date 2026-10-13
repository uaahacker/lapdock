using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LapDock.Services;
using WinRtShare = Windows.ApplicationModel.DataTransfer;
using WinRtStorage = Windows.Storage;

namespace LapDock.Views.Tabs;

/// <summary>
/// The shelf: files, links and text dropped on the notch, kept for later (files by reference — never copied).
/// Drag an item back out into any app, open it, show it in its folder, share it with Windows' share sheet,
/// copy it, or show a QR code: links and text directly, files through <see cref="PhoneShareServer"/>.
/// </summary>
public partial class ShelfTab : UserControl
{
    private readonly ObservableCollection<ShelfItem> _items = new(ShelfService.Load());
    private PhoneShareServer? _server;
    private ShelfItem? _dragItem, _shareItem;
    private Point _dragFrom;
    private bool _shareHooked;

    /// <summary>An item is on its way out (dragged from the shelf): the notch isn't a drop target meanwhile.</summary>
    public bool IsDraggingOut { get; private set; }

    /// <summary>Dragging an item out: the notch closes, so it doesn't cover where the item is going.</summary>
    public event Action? DragOutStarted;

    public ShelfTab()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        Update();
    }

    private ShelfItem? Selected => List.SelectedItem as ShelfItem;

    /// <summary>The notch opened on this tab: files may have been moved or resized since.</summary>
    public void Refresh()
    {
        List.Items.Refresh();
        Update();
    }

    // ------------------------------------------------------------------ in
    /// <summary>Can the shelf take what's being dragged over the notch?</summary>
    public static bool Accepts(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);

    /// <summary>Something is being dragged over the notch: light up the drop zone.</summary>
    public void ShowDropTarget(bool on)
    {
        var brush = (System.Windows.Media.Brush)FindResource(on ? "AccentBrush" : "FillHoverBrush");
        EmptyDash.Stroke = brush;
        EmptyIcon.Foreground = on ? brush : (System.Windows.Media.Brush)FindResource("MutedBrush");
        EmptyTitle.Text = on ? "Drop to keep it here" : "Drop files here";
        ListCard.BorderBrush = on ? brush : System.Windows.Media.Brushes.Transparent;
    }

    public void Add(IDataObject data)
    {
        ShowDropTarget(false);
        var added = new List<ShelfItem>();
        if (data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
                if (!_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                    added.Add(new ShelfItem { Path = path });
        }
        else if ((data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) is string text && text.Trim() is { Length: > 0 } trimmed)
        {
            if (!_items.Any(i => i.Text == trimmed)) added.Add(new ShelfItem { Text = trimmed });
        }
        for (int i = added.Count - 1; i >= 0; i--) _items.Insert(0, added[i]); // newest first, in the order they were dropped
        if (added.Count > 0)
        {
            ShelfService.Save(_items);
            List.SelectedItem = added[0];
        }
        Update();
        Status(added.Count switch { 0 => "Already on the shelf", 1 => "Added to the shelf", _ => $"Added {added.Count} items" });
    }

    // ----------------------------------------------------------------- out
    private void List_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragItem = (e.OriginalSource as FrameworkElement)?.DataContext as ShelfItem;
        _dragFrom = e.GetPosition(List);
    }

    /// <summary>Drag an item out of the shelf into any app (Explorer, mail, chat…). The original stays where it is.</summary>
    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is not { Exists: true } item) return;
        var moved = e.GetPosition(List) - _dragFrom;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragItem = null;
        var data = new DataObject();
        if (item.Path is { } path) data.SetFileDropList(new StringCollection { path });
        else data.SetText(item.Text ?? "");
        IsDraggingOut = true;
        DragOutStarted?.Invoke();
        try { DragDrop.DoDragDrop(List, data, DragDropEffects.Copy | DragDropEffects.Link); }
        catch (Exception) { /* the target refused it */ }
        finally { IsDraggingOut = false; }
    }

    // ------------------------------------------------------------- details
    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HideQr();
        ShowDetails();
    }

    private void Update()
    {
        bool any = _items.Count > 0;
        EmptyZone.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        Filled.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (any && Selected is null) List.SelectedIndex = 0;
        StatusText.Text = any
            ? $"{(_items.Count == 1 ? "1 item" : $"{_items.Count} items")} · drag one out into any app"
            : "Files stay where they are: the shelf only remembers them";
        ShowDetails();
    }

    private void ShowDetails()
    {
        if (Selected is not { } item) return;
        DetailIcon.Source = item.Icon;
        DetailGlyph.Icon = item.Icon is null ? item.Glyph : null;
        DetailName.Text = item.Name;
        DetailMeta.Text = item.Path is { } path ? $"{item.Detail} · {Path.GetDirectoryName(path.TrimEnd('\\')) ?? path}" : item.Detail;
        DetailMeta.ToolTip = item.Path ?? item.Text;
        OpenButton.Visibility = Show(item.Exists && (item.IsFile || item.Url is not null));
        FolderButton.Visibility = Show(item.IsFile && item.Exists);
        ShareButton.Visibility = Show(item.Exists);
        QrButton.Visibility = Show(!item.IsFolder && item.Exists);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void Status(string text) => StatusText.Text = text;

    // ------------------------------------------------------------- actions
    private void Open_Click(object sender, RoutedEventArgs e) => Open(Selected);
    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Open(Selected);

    private void Open(ShelfItem? item)
    {
        if (item is null || !item.Exists || (item.Path ?? item.Url) is not { } target) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { Status("Windows couldn't open it."); }
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (Selected?.Path is { } path) DownloadWatcher.ShowInFolder(path);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        try
        {
            if (item.Path is { } path) Clipboard.SetFileDropList(new StringCollection { path });
            else Clipboard.SetText(item.Text ?? "");
            Status(item.IsFile ? "Copied: paste it into a folder or a message" : "Copied");
        }
        catch (Exception) { Status("The clipboard is busy. Try again."); }
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => Remove(Selected);

    private void Remove(ShelfItem? item)
    {
        if (item is null) return;
        int index = List.SelectedIndex;
        _items.Remove(item);
        ShelfService.Save(_items);
        if (_items.Count > 0) List.SelectedIndex = Math.Min(index, _items.Count - 1);
        Update();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        HideQr();
        _items.Clear();
        ShelfService.Save(_items);
        Update();
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) Remove(Selected);
        else if (e.Key == Key.Enter) Open(Selected);
        else return;
        e.Handled = true;
    }

    // --------------------------------------------------------------- share
    /// <summary>Windows' share sheet: nearby devices, Phone Link, mail, WhatsApp and other apps that take files.</summary>
    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        try
        {
            var hwnd = new WindowInteropHelper(Window.GetWindow(this)!).Handle;
            var manager = WinRtShare.DataTransferManagerInterop.GetForWindow(hwnd);
            if (!_shareHooked)
            {
                manager.DataRequested += OnShareRequested;
                _shareHooked = true;
            }
            _shareItem = item;
            WinRtShare.DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception) { Status("Sharing isn't available on this PC."); }
    }

    private async void OnShareRequested(WinRtShare.DataTransferManager sender, WinRtShare.DataRequestedEventArgs e)
    {
        if (_shareItem is not { } item) return;
        var deferral = e.Request.GetDeferral();
        try
        {
            e.Request.Data.Properties.Title = item.Name;
            if (item.Path is { } path)
            {
                WinRtStorage.IStorageItem storage = item.IsFolder
                    ? await WinRtStorage.StorageFolder.GetFolderFromPathAsync(path)
                    : await WinRtStorage.StorageFile.GetFileFromPathAsync(path);
                e.Request.Data.SetStorageItems(new[] { storage });
            }
            else if (item.Url is { } url) e.Request.Data.SetWebLink(new Uri(url));
            else e.Request.Data.SetText(item.Text ?? "");
        }
        catch (Exception) { e.Request.FailWithDisplayText("LapDock couldn't share this item."); }
        finally { deferral.Complete(); }
    }

    // ------------------------------------------------------------------ QR
    private void Qr_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        HideQr();
        string payload;
        if (item.Path is { } path)
        {
            _server = PhoneShareServer.Start(path);
            if (_server is null)
            {
                Status("Connect to Wi-Fi first: your phone downloads it over the network.");
                return;
            }
            payload = _server.Url;
            QrText.Text = "Scan with your phone's camera to download it. Same Wi-Fi network · the link stops in 10 minutes.";
        }
        else
        {
            payload = item.Url ?? item.Text ?? "";
            QrText.Text = item.Url is not null ? "Scan with your phone's camera to open the link." : "Scan with your phone's camera to get this text.";
        }
        QrImage.Source = ClipboardTab.MakeQr(payload);
        if (QrImage.Source is null)
        {
            HideQr();
            Status("Too long for a QR code (about 2,000 characters at most).");
            return;
        }
        Details.Visibility = Visibility.Collapsed;
        QrPanel.Visibility = Visibility.Visible;
    }

    private void QrDone_Click(object sender, RoutedEventArgs e) => HideQr();

    private void HideQr()
    {
        _server?.Dispose(); // the link stops working with the code
        _server = null;
        QrPanel.Visibility = Visibility.Collapsed;
        Details.Visibility = Visibility.Visible;
    }
}
