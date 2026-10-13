using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace LapDock.Services;

/// <summary>Something put on the shelf: a file or folder (held by its path, never copied), or text / a link.</summary>
public sealed class ShelfItem
{
    public string? Path { get; set; }
    public string? Text { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;

    [JsonIgnore] public bool IsFile => Path is not null;
    [JsonIgnore] public bool IsFolder => Path is not null && Directory.Exists(Path);
    [JsonIgnore] public bool Exists => Path is null || File.Exists(Path) || Directory.Exists(Path);
    [JsonIgnore] public string? Url => Text is { } t && Uri.TryCreate(t.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u.AbsoluteUri : null;

    [JsonIgnore]
    public string Name => Path is not null
        ? System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } name ? name : Path
        : Url is { } url ? url[(url.IndexOf("//", StringComparison.Ordinal) + 2)..].Replace("www.", "").TrimEnd('/') // "github.com/you/repo"
        : (Text ?? "").ReplaceLineEndings(" ").Trim();

    /// <summary>The file's own icon, from the shell (null for text, links and missing files).</summary>
    [JsonIgnore] public ImageSource? Icon => Path is not null && Exists ? ShellIconService.Get(Path, 64).Icon : null;
    /// <summary>Drawn when there's no icon.</summary>
    [JsonIgnore] public string Glyph => Path is not null ? "folder" : Url is not null ? "link" : "clipboard";

    [JsonIgnore]
    public string Detail
    {
        get
        {
            if (IsFolder) return "Folder";
            if (Path is not null) return File.Exists(Path) ? DownloadWatcher.FormatBytes(new FileInfo(Path).Length) : "Moved or deleted";
            return Url is not null ? "Link" : "Text";
        }
    }
}

/// <summary>The shelf's items, in shelf.json in the data folder.</summary>
public static class ShelfService
{
    private static readonly string FilePath = Path.Combine(SettingsService.Dir, "shelf.json");

    public static List<ShelfItem> Load()
    {
        try { return JsonSerializer.Deserialize<List<ShelfItem>>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception) { return new(); }
    }

    public static void Save(IEnumerable<ShelfItem> items)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items.ToList()));
        }
        catch (Exception) { }
    }
}
