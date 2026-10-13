using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace LapDock.Services;

public enum WallpaperStyle { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>
/// Desktop wallpaper through the shell's own IDesktopWallpaper (what Settings → Personalization
/// uses): get/set the picture on every display and its fit style. Images are used in place,
/// never copied. Thumbnails are decoded small, off the UI thread, and cached.
/// </summary>
public static class WallpaperService
{
    public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".jfif", ".gif", ".tif", ".tiff" };

    private static readonly ConcurrentDictionary<(string, DateTime, int), BitmapSource?> Thumbs = new();
    private static readonly string ThumbDir = Path.Combine(SettingsService.Dir, "thumbs");

    /// <summary>Current picture wallpaper, or null for a solid colour / Spotlight / unreadable.</summary>
    public static string? Current()
    {
        try
        {
            var wp = Create();
            try
            {
                var path = wp.GetWallpaper(null); // one picture shared by all displays
                if (string.IsNullOrEmpty(path) && wp.GetMonitorDevicePathCount() > 0)
                    path = wp.GetWallpaper(wp.GetMonitorDevicePathAt(0)); // per-display pictures: show the first
                return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : path;
            }
            finally { Marshal.ReleaseComObject(wp); }
        }
        catch (Exception) { return null; }
    }

    public static WallpaperStyle? CurrentStyle()
    {
        try
        {
            var wp = Create();
            try { return wp.GetPosition(); }
            finally { Marshal.ReleaseComObject(wp); }
        }
        catch (Exception) { return null; }
    }

    /// <summary>Applies on a worker thread (the shell transcodes the image, which can take a moment).</summary>
    public static Task Apply(string path, WallpaperStyle style) => Task.Run(() =>
    {
        var wp = Create();
        try
        {
            wp.SetPosition(style);
            wp.SetWallpaper(null, path); // null = every display
        }
        finally { Marshal.ReleaseComObject(wp); }
    });

    public static Task SetStyle(WallpaperStyle style) => Task.Run(() =>
    {
        var wp = Create();
        try { wp.SetPosition(style); }
        finally { Marshal.ReleaseComObject(wp); }
    });

    public static bool IsImage(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Decoded preview, cached per file version and size. Thumbnails (≤ 400 px) are also kept as small
    /// JPEGs in %AppData%/LapDock/thumbs, so a grid of 100+ pictures fills quickly after a restart.
    /// ponytail: that folder is never pruned (a few KB per picture); clear it if it ever matters.
    /// </summary>
    public static Task<BitmapSource?> Thumbnail(string path, int width) => Task.Run(() =>
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            return Thumbs.GetOrAdd((path, stamp, width), _ => LoadThumbnail(path, stamp, width));
        }
        catch (Exception) { return null; }
    });

    private static BitmapSource? LoadThumbnail(string path, DateTime stamp, int width)
    {
        bool small = width <= 400;
        var cached = Path.Combine(ThumbDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{path}|{stamp.Ticks}|{width}"))) + ".jpg");
        if (small && File.Exists(cached) && Decode(cached, 0) is { } hit) return hit;
        var image = Decode(path, width);
        if (image is null || !small) return image;
        try
        {
            Directory.CreateDirectory(ThumbDir);
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(cached);
            encoder.Save(file);
        }
        catch (Exception) { /* no cache this time */ }
        return image;
    }

    private static BitmapSource? Decode(string path, int width)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            if (width > 0) image.DecodePixelWidth = width; // decode small: a 4K wallpaper never sits in memory
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Pictures for the Wallpaper tab: recent ones first, then Pictures (top level), Pictures\Wallpapers,
    /// Windows' own wallpapers, the current wallpaper's folder and any added folders. Skips files under
    /// 40 KB (icons, thumbnails) and stops at 1,000 pictures.
    /// </summary>
    public static List<string> FindPictures(IEnumerable<string> addedFolders, IEnumerable<string> recent, string? current)
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var sources = new List<(string Folder, int Depth)>
        {
            (pictures, 0),
            (Path.Combine(pictures, "Wallpapers"), 3),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper"), 3),
        };
        if (Path.GetDirectoryName(current) is { } currentFolder) sources.Add((currentFolder, 0));
        sources.AddRange(addedFolders.Select(f => (f, 3)));

        var found = recent.Where(File.Exists).ToList();
        foreach (var (folder, depth) in sources) Collect(folder, depth, found);
        return found.Distinct(StringComparer.OrdinalIgnoreCase).Take(1000).ToList();
    }

    private static void Collect(string folder, int depth, List<string> found)
    {
        if (found.Count >= 1000 || !Directory.Exists(folder)) return;
        try
        {
            found.AddRange(new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => IsImage(f.FullName) && f.Length >= 40_000 && (f.Attributes & FileAttributes.Hidden) == 0)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName));
            if (depth > 0)
                foreach (var sub in Directory.EnumerateDirectories(folder)) Collect(sub, depth - 1, found);
        }
        catch (Exception) { /* access denied: skip it */ }
    }

    private static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

    // ------------------------------------------------------------------ interop
    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out RECT rect);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(WallpaperStyle position);
        WallpaperStyle GetPosition();
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
}
