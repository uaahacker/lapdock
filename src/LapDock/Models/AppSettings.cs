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
    /// Dock pins: "Label|Target" where Target is an exe/lnk path, a shell: URI, or a plain app name for PATH lookup.
    /// The real app icon is pulled from the shell; Label is the fallback when there is no icon.
    /// Drag files/shortcuts onto the dock to add, right-click an icon to remove.
    /// </summary>
    public List<string> PinnedApps { get; set; } = new()
    {
        "File Explorer|explorer.exe",
        "Edge|shell:AppsFolder\\MSEdge",
        "Terminal|shell:AppsFolder\\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App",
        "Spotify|%APPDATA%\\Spotify\\Spotify.exe",
        "Settings|ms-settings:",
    };

    // ── Notch widgets: each one can be switched off independently. ──
    public bool ShowClock { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool MicrophoneIndicator { get; set; } = true;
    public bool CameraIndicator { get; set; } = true;
    public bool MediaControls { get; set; } = true;
    /// <summary>Short-lived notch messages: charging, low battery, mic in use, screenshot saved…</summary>
    public bool DynamicNotifications { get; set; } = true;

    /// <summary>Recently applied wallpapers (paths only — files are never copied), newest first.</summary>
    public List<string> RecentWallpapers { get; set; } = new();

    /// <summary>
    /// v0.1's emoji pins: their Edge/Terminal targets give no real icon on current Windows.
    /// Pins could not be edited in v0.1, so an exact match is safely upgraded on load.
    /// </summary>
    public static readonly string[] V01Pins =
    {
        "📁|explorer.exe",
        "🌐|shell:AppsFolder\\Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MicrosoftEdge",
        "💻|wt.exe",
        "🎵|shell:AppsFolder\\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
        "⚙️|ms-settings:",
    };
}
