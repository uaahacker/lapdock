using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LapDock.Services;

public sealed class CalendarEvent
{
    public DateTime Start { get; set; }
    public string Title { get; set; } = "";
    public bool AllDay { get; set; }
}

/// <summary>
/// Local calendar: %AppData%/LapDock/events.json (hand-editable), filled by importing .ics files.
/// ponytail: the ICS reader takes DTSTART + SUMMARY only — no RRULE (a weekly meeting imports
/// once), TZID times are read as local time. Add recurrence if imported calendars need it.
/// </summary>
public static class CalendarService
{
    private static readonly string FilePath = Path.Combine(SettingsService.Dir, "events.json");

    public static List<CalendarEvent> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<CalendarEvent>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* corrupt file: show an empty calendar rather than nothing at all */ }
        return new();
    }

    /// <summary>Adds the file's events (skipping ones already there). Returns how many were new.</summary>
    public static int Import(string icsPath)
    {
        var events = Load();
        int added = 0;
        foreach (var e in ParseIcs(File.ReadAllText(icsPath)))
        {
            if (events.Any(x => x.Start == e.Start && x.Title == e.Title)) continue;
            events.Add(e);
            added++;
        }
        Directory.CreateDirectory(SettingsService.Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(events.OrderBy(e => e.Start), new JsonSerializerOptions { WriteIndented = true }));
        return added;
    }

    public static List<CalendarEvent> ParseIcs(string text)
    {
        // RFC 5545 folding: a line starting with a space or tab continues the previous one.
        text = text.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");
        var result = new List<CalendarEvent>();
        CalendarEvent? current = null;
        foreach (var line in text.Split('\n'))
        {
            if (line == "BEGIN:VEVENT") { current = new CalendarEvent { Start = DateTime.MinValue }; continue; }
            if (current is null) continue;
            if (line == "END:VEVENT")
            {
                if (current.Start != DateTime.MinValue) result.Add(current);
                current = null;
                continue;
            }
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string name = line[..colon], value = line[(colon + 1)..].Trim();
            if (name == "SUMMARY" || name.StartsWith("SUMMARY;"))
                current.Title = value.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
            else if ((name == "DTSTART" || name.StartsWith("DTSTART;")) && ParseDate(value) is { } start)
                (current.Start, current.AllDay) = start;
        }
        return result;
    }

    private static (DateTime, bool)? ParseDate(string v)
    {
        var inv = CultureInfo.InvariantCulture;
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", inv, DateTimeStyles.None, out var day))
            return (day, true);
        if (v.EndsWith('Z') && DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss'Z'", inv,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            return (utc.ToLocalTime(), false);
        if (DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss", inv, DateTimeStyles.None, out var local))
            return (local, false);
        return null;
    }

    /// <summary>Debug-build self-check of the ICS reader (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        var ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;TZID=Asia/Karachi:20261014T100000\r\nSUMMARY:Stand\r\n up\\, daily\r\nEND:VEVENT\r\n"
                + "BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20261016\r\nSUMMARY:Holiday\r\nEND:VEVENT\r\n"
                + "BEGIN:VEVENT\r\nSUMMARY:No start\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var e = ParseIcs(ics);
        Debug.Assert(e.Count == 2);
        Debug.Assert(e[0].Title == "Standup, daily" && e[0].Start == new DateTime(2026, 10, 14, 10, 0, 0) && !e[0].AllDay);
        Debug.Assert(e[1].Title == "Holiday" && e[1].Start == new DateTime(2026, 10, 16) && e[1].AllDay);
        var utc = ParseIcs("BEGIN:VEVENT\nDTSTART:20261014T050000Z\nSUMMARY:U\nEND:VEVENT\n");
        Debug.Assert(utc[0].Start == new DateTime(2026, 10, 14, 5, 0, 0, DateTimeKind.Utc).ToLocalTime());
    }
}
