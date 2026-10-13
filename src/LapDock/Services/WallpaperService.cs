using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
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

    private static readonly ConcurrentDictionary<(string, DateTime), BitmapSource?> Thumbs = new();

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

    /// <summary>
    /// "Next": the next picture (by name) in the current wallpaper's folder; if that folder
    /// has no other pictures, the next one in the recent list. Null when there is nowhere to go.
    /// </summary>
    public static string? Next(string? current, IReadOnlyList<string> recent)
    {
        try
        {
            if (current is not null && Path.GetDirectoryName(current) is { } dir)
            {
                var siblings = Directory.EnumerateFiles(dir)
                    .Where(IsImage)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (siblings.Count > 1)
                {
                    int i = siblings.FindIndex(f => f.Equals(current, StringComparison.OrdinalIgnoreCase));
                    return siblings[(i + 1) % siblings.Count];
                }
            }
        }
        catch (Exception) { /* unreadable folder — fall through to recents */ }

        var others = recent.Where(File.Exists).ToList();
        if (others.Count == 0) return null;
        int at = current is null ? -1 : others.FindIndex(f => f.Equals(current, StringComparison.OrdinalIgnoreCase));
        var next = others[(at + 1) % others.Count];
        return next.Equals(current, StringComparison.OrdinalIgnoreCase) ? null : next;
    }

    public static bool IsImage(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Small decoded preview, cached per file version. Null if the image can't be decoded.</summary>
    public static Task<BitmapSource?> Thumbnail(string path, int width) => Task.Run(() =>
    {
        try
        {
            var key = (path, File.GetLastWriteTimeUtc(path));
            return Thumbs.GetOrAdd(key, _ =>
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(path);
                    image.DecodePixelWidth = width; // decode small: a 4K wallpaper never sits in memory
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch (Exception) { return null; }
            });
        }
        catch (Exception) { return null; }
    });

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
