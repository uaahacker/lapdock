using System.IO;
using System.Text.Json;
using LapDock.Models;

namespace LapDock.Services;

public static class SettingsService
{
    /// <summary>%AppData%/LapDock — settings.json plus the other stores (todos.json, events.json…).</summary>
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LapDock");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                if (settings.SummonHotkey == AppSettings.OldSummonHotkey)
                    settings.SummonHotkey = AppSettings.DefaultSummonHotkey;
                return settings;
            }
        }
        catch { /* corrupted settings must never stop the dock from starting */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
