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

    /// <summary>Dock pins: "Name|Target" where Target is an exe path, a shell: URI, or a plain app name for PATH lookup.</summary>
    public List<string> PinnedApps { get; set; } = new()
    {
        "📁|explorer.exe",
        "🌐|shell:AppsFolder\\Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MicrosoftEdge",
        "💻|wt.exe",
        "🎵|shell:AppsFolder\\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
        "⚙️|ms-settings:",
    };
}
