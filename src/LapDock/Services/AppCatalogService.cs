using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LapDock.Services;

/// <summary>An app as Start lists it. Target launches it: <c>shell:AppsFolder\&lt;id&gt;</c>.</summary>
public sealed record AppEntry(string Name, string Target);

public sealed record Snippet(string Name, string Text);

/// <summary>
/// The launcher's data: every app in Windows' Apps folder (exactly what Start lists — desktop and
/// Store apps alike, no .lnk parsing), favourites, snippets, and a small file-name index.
/// The app list is cached in apps-cache.json so the tab opens instantly, then refreshed in the background.
/// </summary>
public static class AppCatalogService
{
    private static readonly string CachePath = Path.Combine(SettingsService.Dir, "apps-cache.json");
    private static readonly string FavouritesPath = Path.Combine(SettingsService.Dir, "favourites.json");
    private static readonly string SnippetsPath = Path.Combine(SettingsService.Dir, "snippets.json");

    // Start lists readmes, uninstallers and web links next to apps; a launcher shouldn't.
    private static readonly string[] NotApps = { ".url", ".txt", ".pdf", ".chm", ".htm", ".html", ".rtf", ".ini", ".log", ".md", ".hlp" };

    public static List<AppEntry> LoadCache() => Read<List<AppEntry>>(CachePath) ?? new();

    /// <summary>Enumerates the Apps folder on an STA thread (shell COM), saves the cache, returns the list.</summary>
    public static Task<List<AppEntry>> RefreshAsync()
    {
        var done = new TaskCompletionSource<List<AppEntry>>();
        var thread = new Thread(() =>
        {
            try
            {
                var apps = Enumerate();
                Write(CachePath, apps);
                done.SetResult(apps);
            }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static List<AppEntry> Enumerate()
    {
        var apps = new List<AppEntry>();
        var folderId = new Guid("1e87508d-89c2-42f0-8a7e-645a0f50ca58"); // FOLDERID_AppsFolder
        var itemId = typeof(IShellItem).GUID;
        SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref itemId, out var folder);
        var bhid = new Guid("94f60519-2850-4924-aa5a-d15e84868039"); // BHID_EnumItems
        var enumId = typeof(IEnumShellItems).GUID;
        folder.BindToHandler(IntPtr.Zero, ref bhid, ref enumId, out var enumerator);
        try
        {
            while (enumerator.Next(1, out var item, out uint fetched) == 0 && fetched == 1)
            {
                try
                {
                    var name = DisplayName(item, 0);                          // SIGDN_NORMALDISPLAY
                    var id = DisplayName(item, unchecked((int)0x80018001));   // SIGDN_PARENTRELATIVEPARSING: AUMID or path
                    if (IsApp(name, id)) apps.Add(new AppEntry(name!, $@"shell:AppsFolder\{id}"));
                }
                finally { Marshal.ReleaseComObject(item); }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(folder);
        }
        return apps.GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool IsApp(string? name, string? id) =>
        !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)
        && !name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
        && !id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        && !NotApps.Any(ext => id.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static string? DisplayName(IShellItem item, int sigdn)
    {
        if (item.GetDisplayName(sigdn, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    // ------------------------------------------------------------- favourites
    public static HashSet<string> LoadFavourites() => new(Read<List<string>>(FavouritesPath) ?? new(), StringComparer.OrdinalIgnoreCase);
    public static void SaveFavourites(IEnumerable<string> targets) => Write(FavouritesPath, targets.ToList());

    // --------------------------------------------------------------- snippets
    /// <summary>snippets.json: [{ "Name": "...", "Text": "..." }] — hand-editable, or saved from the Clipboard tab.</summary>
    public static List<Snippet> LoadSnippets() => Read<List<Snippet>>(SnippetsPath) ?? new();

    public static void AddSnippet(string text)
    {
        var snippets = LoadSnippets();
        if (snippets.Any(s => s.Text == text)) return;
        var firstLine = text.Trim().Split('\n')[0].Trim();
        snippets.Add(new Snippet(firstLine.Length > 40 ? firstLine[..40] + "…" : firstLine, text));
        Write(SnippetsPath, snippets);
    }

    public static void RemoveSnippet(Snippet snippet) => Write(SnippetsPath, LoadSnippets().Where(s => s != snippet).ToList());

    // -------------------------------------------------------------- matching
    /// <summary>
    /// How well <paramref name="query"/> matches a name, 0 = not at all. Exact name beats a whole word
    /// ("code" → Visual Studio Code, ahead of "Codec…") beats prefix beats word start beats initials
    /// ("vsc") beats substring beats letters-in-order ("gchr" → Google Chrome). Ties: shorter name first.
    /// </summary>
    public static int Score(string name, string query)
    {
        if (query.Length == 0) return 1;
        var n = name.ToLowerInvariant();
        var q = query.Trim().ToLowerInvariant();
        if (n == q) return 120;
        var words = n.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains(q)) return 95;
        if (n.StartsWith(q)) return 90;
        if (words.Any(w => w.StartsWith(q))) return 80;
        if (q.Length > 1 && string.Concat(words.Select(w => w[0])).StartsWith(q)) return 70;
        if (n.Contains(q)) return 50;
        // Letters in order, starting at a word ("gchr" → Google Chrome) — not anywhere ("code" ≠ reCOrDEr).
        if (q.Length < 3) return 0;
        foreach (var start in words.Where(w => w[0] == q[0]))
        {
            int i = 0;
            foreach (var c in n[n.IndexOf(start, StringComparison.Ordinal)..]) if (i < q.Length && c == q[i]) i++;
            if (i == q.Length) return 20;
        }
        return 0;
    }

    // ------------------------------------------------------------ file names
    /// <summary>
    /// File names under the index folders, for "files" results. ponytail: names only, depth 4, at most
    /// 20,000 entries, skipping hidden and build folders — no content search or full-disk index.
    /// </summary>
    public static List<string> IndexFiles(IEnumerable<string> folders)
    {
        var files = new List<string>();
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "packages", "venv", "__pycache__" };
        void Walk(string dir, int depth)
        {
            if (files.Count >= 20_000) return;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if ((File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    files.Add(f);
                    if (files.Count >= 20_000) return;
                }
                if (depth == 0) return;
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(d);
                    if (name.StartsWith('.') || skip.Contains(name)) continue;
                    if ((File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                    Walk(d, depth - 1);
                }
            }
            catch (Exception) { /* access denied, vanished: skip that folder */ }
        }
        foreach (var folder in folders.Where(Directory.Exists)) Walk(folder, 4);
        return files;
    }

    // ------------------------------------------------------------------ json
    private static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch (Exception) { return default; } // corrupt: start empty rather than fail
    }

    private static void Write<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.Dir);
            File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* best effort */ }
    }

    /// <summary>Debug-build self-check of the matching rules (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        Debug.Assert(Score("Visual Studio Code", "code") > Score("Codec Tweak Tool", "code")); // whole word beats prefix
        Debug.Assert(Score("Visual Studio Code", "vsc") == 70);
        Debug.Assert(Score("Visual Studio Code", "vis") == 90);
        Debug.Assert(Score("Visual Studio Code", "stu") == 80);
        Debug.Assert(Score("Settings", "settings") == 120);
        Debug.Assert(Score("Google Chrome", "gchr") == 20);
        Debug.Assert(Score("Google Chrome", "oogle") == 50);
        Debug.Assert(Score("Discord", "xyz") == 0);
        Debug.Assert(Score("Sound Recorder", "code") == 0 && Score("Microsoft Edge", "code") == 0); // scattered letters: no
        Debug.Assert(Score("Discord", "") == 1);
        Debug.Assert(IsApp("Code", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Microsoft VS Code\Code.exe"));
        Debug.Assert(!IsApp("Uninstall Foo", "Foo.exe") && !IsApp("Readme", @"C:\x\readme.txt") && !IsApp("Site", "https://x.y"));
    }

    // ---------------------------------------------------------------- interop
    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetKnownFolderItem(ref Guid folderId, int flags, IntPtr token, ref Guid riid, out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IEnumShellItems result);
        void GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(int sigdn, out IntPtr name);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, out IShellItem item, out uint fetched);
    }
}
