using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LapDock.Services;
using QRCoder;
using QRCoder.Exceptions;

namespace LapDock.Views.Tabs;

/// <summary>
/// Clipboard history: search, Text / Images / Links filters, a list and a preview with
/// Copy, Pin, QR, Open (links) and Delete. Enter or double-click pastes into the app you were in.
/// </summary>
public partial class ClipboardTab : UserControl
{
    private static readonly (string Label, Func<ClipItem, bool> Match)[] Filters =
    {
        ("All", _ => true),
        ("Text", i => !i.IsImage && !i.IsLink),
        ("Images", i => i.IsImage),
        ("Links", i => i.IsLink),
    };

    private readonly DispatcherTimer _confirmReset = new() { Interval = TimeSpan.FromSeconds(4) };
    private ClipboardService? _clips;
    private ListCollectionView? _view;
    private string _hotkey = "";
    private int _filter;
    private bool _qr, _confirmClear;

    /// <summary>Enter / double-click: copy this item and paste it into the previous app.</summary>
    public event Action<ClipItem>? PasteRequested;
    /// <summary>Esc with an empty search: close the notch.</summary>
    public event Action? CloseRequested;

    public ClipboardTab()
    {
        InitializeComponent();
        _confirmReset.Tick += (_, _) => ResetClear();
        for (int i = 0; i < Filters.Length; i++)
        {
            int index = i;
            var chip = new Button { Content = Filters[i].Label, Style = (Style)FindResource("FilterChip") };
            chip.Click += (_, _) => { _filter = index; Refilter(); };
            Chips.Children.Add(chip);
        }
        UpdateChips();
        ShowPreview();
    }

    public void Attach(ClipboardService clips, string hotkey)
    {
        _clips = clips;
        _hotkey = hotkey;
        _view = new ListCollectionView(clips.Items) { Filter = o => Matches((ClipItem)o) };
        List.ItemsSource = _view;
        clips.Items.CollectionChanged += (_, _) => UpdateStatus();
        UpdateStatus();
        SelectFirst();
    }

    public void SetHotkey(string hotkey)
    {
        _hotkey = hotkey;
        UpdateStatus();
    }

    /// <summary>Opened from the shortcut: type to search straight away.</summary>
    public void FocusSearch()
    {
        SearchBox.SelectAll();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        if (List.SelectedIndex < 0) SelectFirst();
    }

    // ---------------------------------------------------------------- filter
    private bool Matches(ClipItem item)
    {
        if (!Filters[_filter].Match(item)) return false;
        var q = SearchBox.Text.Trim();
        return q.Length == 0 || (item.Text?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private void Refilter()
    {
        _view?.Refresh();
        UpdateChips();
        SelectFirst();
        UpdateStatus();
    }

    private void UpdateChips()
    {
        for (int i = 0; i < Chips.Children.Count; i++) ((Button)Chips.Children[i]).Tag = i == _filter ? "on" : null;
    }

    private void SelectFirst()
    {
        List.SelectedIndex = List.Items.Count > 0 ? 0 : -1;
        if (List.SelectedItem is not null) List.ScrollIntoView(List.SelectedItem);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refilter();

    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter: if (Selected is { } item) PasteRequested?.Invoke(item); e.Handled = true; break;
            case Key.Escape:
                if (SearchBox.Text.Length > 0) SearchBox.Clear();
                else CloseRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (List.Items.Count == 0) return;
        List.SelectedIndex = Math.Clamp(List.SelectedIndex + delta, 0, List.Items.Count - 1);
        List.ScrollIntoView(List.SelectedItem);
    }

    // ------------------------------------------------------------------ list
    private ClipItem? Selected => List.SelectedItem as ClipItem;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _qr = false;
        ShowPreview();
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } item) PasteRequested?.Invoke(item);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is { } item) PasteRequested?.Invoke(item);
        else if (e.Key == Key.Delete) Delete_Click(sender, e);
        else if (e.Key == Key.Escape) CloseRequested?.Invoke();
    }

    // --------------------------------------------------------------- preview
    private void ShowPreview()
    {
        var item = Selected;
        PreviewText.Visibility = Show(item is { IsImage: false } && !_qr);
        PreviewImageHost.Visibility = Show(item is { IsImage: true } && !_qr);
        QrHost.Visibility = Show(_qr);
        Actions.Visibility = Show(item is not null);
        PreviewEmpty.Visibility = Show(item is null);
        PreviewEmpty.Text = _clips is { Items.Count: > 0 } ? "Nothing matches. Try another word or filter." : "Copy some text or an image and it shows up here.";
        if (item is null) { PreviewMeta.Text = ""; return; }

        PinLabel.Text = item.Pinned ? "Unpin" : "Pin";
        QrLabel.Text = _qr ? "Hide QR" : "QR";
        QrButton.Visibility = Show(!item.IsImage);
        SnippetButton.Visibility = Show(!item.IsImage);
        SnippetLabel.Text = AppCatalogService.LoadSnippets().Any(s => s.Text == item.Text) ? "Saved as snippet" : "Snippet";
        OpenButton.Visibility = Show(item.IsLink);
        CopyLabel.Text = "Copy";
        var when = item.Copied.Date == DateTime.Today ? $"today {item.Copied:t}" : item.Copied.ToString("d MMM, t");
        PreviewMeta.Text = item.IsImage
            ? $"Image, {item.Width} × {item.Height}, copied {when}"
            : $"{item.Text!.Length:N0} characters, copied {when}";
        if (!item.IsImage && !_qr) PreviewText.Text = item.Text;
        if (item.IsImage && !_qr) PreviewImage.Source = ClipboardService.LoadImage(item.ImagePath, 600);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item || _clips is null) return;
        _clips.Copy(item);
        CopyLabel.Text = "Copied";
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item || _clips is null) return;
        _clips.SetPinned(item, !item.Pinned);
        List.SelectedItem = item;
        List.ScrollIntoView(item);
        ShowPreview();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item || _clips is null) return;
        int index = List.SelectedIndex;
        _clips.Delete(item);
        if (List.Items.Count > 0) List.SelectedIndex = Math.Min(index, List.Items.Count - 1);
    }

    private void Qr_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Text: { } text }) return;
        _qr = !_qr;
        if (_qr)
        {
            QrImage.Source = MakeQr(text);
            if (QrImage.Source is null)
            {
                _qr = false;
                PreviewMeta.Text = "Too long for a QR code (about 2,000 characters at most).";
                return;
            }
        }
        ShowPreview();
    }

    private void Snippet_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Text: { } text }) return;
        AppCatalogService.AddSnippet(text);
        SnippetLabel.Text = "Saved as snippet";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { IsLink: true, Text: { } text }) return;
        var url = text.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { PreviewMeta.Text = "Couldn't open that link."; }
    }

    internal static BitmapImage? MakeQr(string text)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(8);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (DataTooLongException) { return null; }
    }

    // ---------------------------------------------------------------- status
    private void UpdateStatus()
    {
        int total = _clips?.Items.Count ?? 0, pinned = _clips?.Items.Count(i => i.Pinned) ?? 0;
        SearchHint.Text = total == 1 ? "Search 1 item…" : $"Search {total:N0} items…";
        SearchHint.Visibility = Show(SearchBox.Text.Length == 0);
        StatusText.Text = $"{_hotkey}  •  {total:N0} items  •  {pinned} pinned";
        ListEmpty.Visibility = Show(List.Items.Count == 0);
        ListEmpty.Text = total == 0 ? "Your copies will appear here." : "No matches";
        ClearButton.Visibility = Show(total > pinned);
        ShowPreview();
    }

    /// <summary>Clearing is permanent: the first click asks, the second clears.</summary>
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!_confirmClear)
        {
            _confirmClear = true;
            ClearButton.Content = "Click again to clear (pinned stay)";
            ClearButton.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            _confirmReset.Start();
            return;
        }
        _clips?.ClearUnpinned();
        ResetClear();
        SelectFirst();
    }

    private void ResetClear()
    {
        _confirmReset.Stop();
        _confirmClear = false;
        ClearButton.Content = "Clear history";
        ClearButton.ClearValue(ForegroundProperty);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
