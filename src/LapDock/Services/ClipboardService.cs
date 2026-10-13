using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LapDock.Models;

namespace LapDock.Services;

public enum ClipKind { Text, Image }

public sealed partial class ClipItem : INotifyPropertyChanged
{
    private bool _pinned;
    private DateTime _copied = DateTime.Now;
    private ImageSource? _thumb;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipKind Kind { get; set; }
    public string? Text { get; set; }
    /// <summary>Image items: PNG file name inside the clipboard folder, plus a content hash for de-duplication.</summary>
    public string? ImageFile { get; set; }
    public string? Hash { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime Copied { get => _copied; set { _copied = value; Notify(); Notify(nameof(TimeText)); } }
    public bool Pinned { get => _pinned; set { _pinned = value; Notify(); } }

    [JsonIgnore] public bool IsImage => Kind == ClipKind.Image;
    [JsonIgnore] public bool IsLink => Kind == ClipKind.Text && Text is not null && Link().IsMatch(Text.Trim());
    [JsonIgnore] public string? ImagePath => ImageFile is null ? null : Path.Combine(ClipboardService.Folder, ImageFile);
    [JsonIgnore] public string TimeText => Copied.Date == DateTime.Today ? Copied.ToString("t") : Copied.ToString("d MMM");

    /// <summary>One line for the list: whitespace collapsed, capped.</summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            if (IsImage) return $"{Width} × {Height} image";
            var line = Whitespace().Replace(Text ?? "", " ").Trim();
            return line.Length > 160 ? line[..160] : line;
        }
    }

    /// <summary>Small list thumbnail, decoded on first display only (the list is virtualised).</summary>
    [JsonIgnore] public ImageSource? Thumb => _thumb ??= IsImage ? ClipboardService.LoadImage(ImagePath, 96) : null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    [GeneratedRegex(@"^(https?://|www\.)\S+$", RegexOptions.IgnoreCase)] private static partial Regex Link();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}

/// <summary>
/// Clipboard history. Windows pushes WM_CLIPBOARDUPDATE to the notch window (AddClipboardFormatListener);
/// each text or image copy is stored in %AppData%/LapDock/clipboard (index.json + one PNG per image).
/// Never recorded: copies that ask not to be (password managers set ExcludeClipboardContentFromMonitorProcessing
/// or CanIncludeInClipboardHistory = 0) and copies from apps in <see cref="AppSettings.ClipboardExcludedApps"/>.
/// Copying something already in history moves it to the top instead of adding a duplicate.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    public static readonly string Folder = Path.Combine(SettingsService.Dir, "clipboard");
    private static readonly string IndexPath = Path.Combine(Folder, "index.json");
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int MaxTextLength = 1_000_000; // ponytail: bigger copies are skipped, not stored in a side file

    private readonly AppSettings _settings;
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;

    /// <summary>Pinned first, then newest first.</summary>
    public ObservableCollection<ClipItem> Items { get; }

    public ClipboardService(Window owner, AppSettings settings)
    {
        _settings = settings;
        Items = new ObservableCollection<ClipItem>(Load());
        _hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(Hook);
        AddClipboardFormatListener(_hwnd);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && _settings.ClipboardHistory) _ = CaptureAsync();
        return IntPtr.Zero;
    }

    /// <summary>The app that just copied may still hold the clipboard open: retry briefly.</summary>
    private async Task CaptureAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Capture(); return; }
            catch (Exception) { await Task.Delay(60); } // CLIPBRD_E_CANT_OPEN and friends
        }
    }

    private void Capture()
    {
        if (Excluded()) return;
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength) AddText(text);
        }
        else if (ReadImage() is { } png) AddImage(png.Bytes, png.Width, png.Height);
    }

    private bool Excluded()
    {
        if (HasFormat("ExcludeClipboardContentFromMonitorProcessing") || HasFormat("Clipboard Viewer Ignore")) return true;
        if (HasFormat("CanIncludeInClipboardHistory") && Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream flag
            && flag.Length >= 4 && BitConverter.ToInt32(flag.ToArray(), 0) == 0) return true;
        try
        {
            GetWindowThreadProcessId(GetClipboardOwner(), out int pid);
            if (pid == 0) return false;
            using var source = Process.GetProcessById(pid);
            return _settings.ClipboardExcludedApps.Contains(source.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; } // the copying app already exited
    }

    /// <summary>PNG bytes of the copied image — the "PNG" format when offered (keeps transparency), else the bitmap.</summary>
    private static (byte[] Bytes, int Width, int Height)? ReadImage()
    {
        if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream stream)
        {
            var bytes = stream.ToArray();
            var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            return (bytes, frame.PixelWidth, frame.PixelHeight);
        }
        if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image) return null;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return (ms.ToArray(), image.PixelWidth, image.PixelHeight);
    }

    private void AddText(string text)
    {
        if (Items.FirstOrDefault(i => i.Kind == ClipKind.Text && i.Text == text) is { } existing) BringToTop(existing);
        else Insert(new ClipItem { Kind = ClipKind.Text, Text = text });
    }

    private void AddImage(byte[] png, int width, int height)
    {
        var hash = Convert.ToHexString(SHA256.HashData(png));
        if (Items.FirstOrDefault(i => i.Hash == hash) is { } existing) { BringToTop(existing); return; }
        var item = new ClipItem { Kind = ClipKind.Image, Hash = hash, Width = width, Height = height };
        item.ImageFile = item.Id + ".png";
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(item.ImagePath!, png);
        Insert(item);
    }

    private void Insert(ClipItem item)
    {
        Items.Insert(Items.TakeWhile(i => i.Pinned).Count(), item);
        Trim();
        Save();
    }

    private void BringToTop(ClipItem item)
    {
        item.Copied = DateTime.Now;
        if (!item.Pinned) Items.Move(Items.IndexOf(item), Items.TakeWhile(i => i.Pinned).Count());
        Save();
    }

    private void Trim()
    {
        while (Items.Count(i => !i.Pinned) > Math.Max(10, _settings.ClipboardLimit))
            Remove(Items.Last(i => !i.Pinned));
    }

    public void SetPinned(ClipItem item, bool pinned)
    {
        item.Pinned = pinned;
        Items.Remove(item);
        // Pinned: to the very top. Unpinned: back among the others by copy time.
        int at = pinned ? 0 : Items.TakeWhile(i => i.Pinned || i.Copied > item.Copied).Count();
        Items.Insert(at, item);
        Save();
    }

    public void Delete(ClipItem item)
    {
        Remove(item);
        Save();
    }

    /// <summary>Clears history but keeps pinned items.</summary>
    public void ClearUnpinned()
    {
        foreach (var item in Items.Where(i => !i.Pinned).ToList()) Remove(item);
        Save();
    }

    private void Remove(ClipItem item)
    {
        Items.Remove(item);
        if (item.ImagePath is { } path)
        {
            try { File.Delete(path); } catch (Exception) { /* locked by a viewer: harmless leftover */ }
        }
    }

    /// <summary>Puts the item back on the clipboard (which moves it to the top of history too).</summary>
    public void Copy(ClipItem item)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (item.IsImage && item.ImagePath is { } path && File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    var data = new DataObject();
                    data.SetImage(LoadImage(path, 0)!);
                    data.SetData("PNG", new MemoryStream(bytes)); // apps that read PNG keep transparency
                    Clipboard.SetDataObject(data, true);
                }
                else if (item.Text is { } text) Clipboard.SetText(text);
                return;
            }
            catch (COMException) { Thread.Sleep(40); }
        }
    }

    /// <summary>Ctrl+V into whatever window has focus.</summary>
    public static void SendPaste()
    {
        const byte VK_CONTROL = 0x11, VK_V = 0x56;
        const int KEYUP = 0x0002;
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_V, 0, 0, 0);
        keybd_event(VK_V, 0, KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYUP, 0);
    }

    /// <summary>Loads a PNG without locking the file; <paramref name="width"/> 0 = full size.</summary>
    public static BitmapImage? LoadImage(string? path, int width)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            if (width > 0) image.DecodePixelWidth = width;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }

    private static List<ClipItem> Load()
    {
        try
        {
            if (File.Exists(IndexPath))
                return (JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(IndexPath)) ?? new())
                    .Where(i => !i.IsImage || File.Exists(i.ImagePath)).ToList();
        }
        catch (Exception) { /* corrupt index: start a fresh history rather than fail */ }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(Items));
        }
        catch (Exception) { /* best effort */ }
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(_hwnd);
        _source.RemoveHook(Hook);
    }

    /// <summary>Debug-build self-check of how items are classified and shown (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        Debug.Assert(new ClipItem { Text = " https://hitodama.in/x?y=1 " }.IsLink);
        Debug.Assert(new ClipItem { Text = "www.example.com" }.IsLink);
        Debug.Assert(!new ClipItem { Text = "see https://a.b for details" }.IsLink); // a sentence with a link is text
        Debug.Assert(new ClipItem { Text = "line one\r\n\tline   two" }.Preview == "line one line two");
        Debug.Assert(new ClipItem { Text = new string('x', 500) }.Preview.Length == 160);
        Debug.Assert(new ClipItem { Kind = ClipKind.Image, Width = 800, Height = 600 }.Preview == "800 × 600 image");
    }

    private static bool HasFormat(string name) => IsClipboardFormatAvailable(RegisterClipboardFormat(name));

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, int flags, int extra);
}
