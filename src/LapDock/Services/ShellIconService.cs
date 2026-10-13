using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LapDock.Services;

/// <summary>
/// Real, high-resolution app icons + display names for dock pins, straight from
/// the Windows shell (same source Explorer and the Start menu use). Works for
/// exe/lnk paths, bare exe names on PATH and shell:AppsFolder\AUMID store apps.
/// </summary>
public static class ShellIconService
{
    private const string SettingsAumid = @"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    private static readonly Dictionary<string, (ImageSource?, string?)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cached per target and size: the dock is rebuilt on every pin/unpin, the shell is asked once.</summary>
    public static (ImageSource? Icon, string? Name) Get(string target, int size = 128) =>
        Cache.TryGetValue($"{size}|{target}", out var hit) ? hit : Cache[$"{size}|{target}"] = Load(target, size);

    private static (ImageSource? Icon, string? Name) Load(string target, int size)
    {
        var parsingName = ToParsingName(target);
        if (parsingName is null) return (null, null);
        IShellItemImageFactory? factory = null;
        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            string? name = null;
            if (factory is IShellItem item && item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var p) == 0)
            {
                name = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
            }
            if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x4 /* BIGGERSIZEOK | ICONONLY */, out var hbm) != 0)
                return (null, name);
            try { return (ToBitmapSource(hbm), name); }
            finally { DeleteObject(hbm); }
        }
        catch (Exception)
        {
            return (null, null); // missing app, uninstalled store package, etc. — caller shows a fallback tile
        }
        finally
        {
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    private static string? ToParsingName(string target)
    {
        target = Environment.ExpandEnvironmentVariables(target.Trim());
        if (target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) return SettingsAumid;
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(target)) return target;
        if (target.Contains(':')) return null; // some other URI scheme — no icon to ask for
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), target);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>
    /// Copies the 32-bit premultiplied DIB into a WPF bitmap top-down.
    /// (Imaging.CreateBitmapSourceFromHBitmap drops the alpha channel → black boxes.)
    /// </summary>
    private static BitmapSource ToBitmapSource(IntPtr hbm)
    {
        var bmp = new BITMAP();
        GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bmp);
        int w = bmp.bmWidth, h = bmp.bmHeight;
        var header = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var px = new byte[w * h * 4];
        var hdc = GetDC(IntPtr.Zero);
        try { GetDIBits(hdc, hbm, 0, (uint)h, px, ref header, 0); }
        finally { ReleaseDC(IntPtr.Zero, hdc); }

        // Legacy icons without an alpha channel would come out invisible.
        bool hasAlpha = false;
        for (int i = 3; i < px.Length && !hasAlpha; i += 4) hasAlpha = px[i] != 0;
        if (!hasAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;

        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        src.Freeze();
        return src;
    }

    // ------------------------------------------------------------------ interop
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, ref BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
}
