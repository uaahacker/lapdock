using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace LapDock.Services;

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    /// <summary>Set = reminder: a notification fires at this local time.</summary>
    public DateTime? Due { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public bool Reminded { get; set; }
}

/// <summary>
/// To-dos and reminders in %AppData%/LapDock/todos.json. "Call mom @18:30" is a reminder for the
/// next 18:30. Reminders fire from an in-app timer aimed at the next due time — only while
/// LapDock runs; one missed while it was closed fires at the next start.
/// </summary>
public sealed partial class TodoService
{
    private static readonly string FilePath = Path.Combine(SettingsService.Dir, "todos.json");
    private readonly DispatcherTimer _timer = new();

    public List<TodoItem> Items { get; }

    /// <summary>UI thread.</summary>
    public event Action<TodoItem>? ReminderDue;

    public TodoService()
    {
        Items = Load();
        _timer.Tick += (_, _) => FireDue();
        Schedule();
    }

    /// <summary>null = nothing to add (blank text).</summary>
    public TodoItem? Add(string input)
    {
        var (text, due) = Parse(input, DateTime.Now);
        if (text.Length == 0) return null;
        var item = new TodoItem { Text = text, Due = due };
        Items.Add(item);
        Changed();
        return item;
    }

    public void SetDone(TodoItem item, bool done)
    {
        item.Done = done;
        Changed();
    }

    public void Remove(TodoItem item)
    {
        Items.Remove(item);
        Changed();
    }

    private void Changed()
    {
        Save();
        Schedule();
    }

    /// <summary>Aim the timer at the next pending reminder (capped at an hour, so sleep or a clock change can't strand it).</summary>
    private void Schedule()
    {
        _timer.Stop();
        var next = Items.Where(Pending).Min(i => i.Due);
        if (next is null) return;
        var wait = next.Value - DateTime.Now;
        _timer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : wait;
        _timer.Start();
    }

    private void FireDue()
    {
        var now = DateTime.Now;
        foreach (var item in Items.Where(i => Pending(i) && i.Due <= now).ToList())
        {
            item.Reminded = true;
            ReminderDue?.Invoke(item);
        }
        Changed();
    }

    private static bool Pending(TodoItem i) => i.Due is not null && !i.Reminded && !i.Done;

    /// <summary>"text @H[:mm]" → (text, next occurrence of that time). Anything else → (text, null).</summary>
    public static (string Text, DateTime? Due) Parse(string input, DateTime now)
    {
        var m = ReminderSuffix().Match(input);
        if (!m.Success) return (input.Trim(), null);
        int hour = int.Parse(m.Groups[2].Value), minute = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        if (hour > 23 || minute > 59) return (input.Trim(), null);
        var due = now.Date.AddHours(hour).AddMinutes(minute);
        if (due <= now) due = due.AddDays(1);
        return (m.Groups[1].Value.Trim(), due);
    }

    [GeneratedRegex(@"^(.*?)\s*@\s*(\d{1,2})(?::(\d{2}))?\s*$")]
    private static partial Regex ReminderSuffix();

    private static List<TodoItem> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* a corrupt file must not stop the notch; it is overwritten on the next change */ }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsService.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }

    /// <summary>Debug-build self-check of the reminder syntax (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        var now = new DateTime(2026, 10, 13, 14, 0, 0);
        Debug.Assert(Parse("Call mom @18:30", now) == ("Call mom", new DateTime(2026, 10, 13, 18, 30, 0)));
        Debug.Assert(Parse("Standup @9", now) == ("Standup", new DateTime(2026, 10, 14, 9, 0, 0)));   // passed → tomorrow
        Debug.Assert(Parse("Mail a@b.com", now) == ("Mail a@b.com", null));                            // not a time
        Debug.Assert(Parse("Bad @25:00", now) == ("Bad @25:00", null));
        Debug.Assert(Parse("  plain  ", now) == ("plain", null));
    }
}
