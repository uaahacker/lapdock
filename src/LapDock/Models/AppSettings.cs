namespace LapDock.Models;

/// <summary>Persisted to %AppData%/LapDock/settings.json by SettingsService.</summary>
public class AppSettings
{
    /// <summary>DeviceName of the display LapDock should cover (e.g. \\.\DISPLAY1). Null = primary.</summary>
    public string? ScreenDeviceName { get; set; }

    /// <summary>Weather location (defaults: Abbottabad, Pakistan).</summary>
    public double Latitude { get; set; } = 34.1688;
    public double Longitude { get; set; } = 73.2215;
    public string CityName { get; set; } = "Abbottabad";

    /// <summary>Scratch-pad text, saved as you type.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Global hotkey that opens/closes Settings (WPF gesture syntax).
    /// Not Ctrl+Alt+Space (Claude desktop) or Win+Alt+Space (PowerToys Command Palette).
    /// </summary>
    public string SummonHotkey { get; set; } = DefaultSummonHotkey;
    public const string DefaultSummonHotkey = "Ctrl+Alt+D";
    /// <summary>The first default, which collides with Claude desktop's quick entry: upgraded on load.</summary>
    public const string OldSummonHotkey = "Ctrl+Alt+Space";

    // ── Clipboard history (Clipboard tab) ──
    public bool ClipboardHistory { get; set; } = true;
    /// <summary>Opens the notch on the Clipboard tab. Note: apps lose their own Ctrl+Shift+V (paste as plain text) while it's set.</summary>
    public string ClipboardHotkey { get; set; } = "Ctrl+Shift+V";
    /// <summary>Unpinned items kept; the oldest go first. Pinned items never count.</summary>
    public int ClipboardLimit { get; set; } = 500;
    /// <summary>Copies from these apps (process names) are never recorded. Password managers also mark their copies themselves.</summary>
    public List<string> ClipboardExcludedApps { get; set; } = new() { "KeePass", "KeePassXC", "1Password", "Bitwarden", "Dashlane", "LastPass" };

    /// <summary>Apps tab file search looks in these folders (names only). Empty = Documents + Desktop.</summary>
    public List<string> LauncherFolders { get; set; } = new();

    /// <summary>Stats tab "Test speed": any large file URL works; its host is also what "Ping" measures.</summary>
    public string SpeedTestUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes=25000000";

    // ── Notch widgets: each one can be switched off independently. ──
    public bool ShowClock { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool MicrophoneIndicator { get; set; } = true;
    public bool CameraIndicator { get; set; } = true;
    public bool MediaControls { get; set; } = true;
    /// <summary>Short-lived notch messages: charging, low battery, mic in use, screenshot saved…</summary>
    public bool DynamicNotifications { get; set; } = true;

    /// <summary>Folders added in the Wallpaper tab ("Add folder"), searched three levels deep.</summary>
    public List<string> WallpaperFolders { get; set; } = new();

    /// <summary>Recently applied wallpapers (paths only — files are never copied), newest first.</summary>
    public List<string> RecentWallpapers { get; set; } = new();
}
