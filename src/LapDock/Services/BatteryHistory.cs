using System.Diagnostics;
using System.Globalization;
using System.IO;
using Windows.Devices.Power;

namespace LapDock.Services;

public readonly record struct BatterySample(DateTime At, int Percent, bool PluggedIn);

/// <param name="Percent">Full-charge capacity as a share of the design capacity (100 = like new).</param>
/// <param name="RateW">Positive while charging, negative while running on battery.</param>
public readonly record struct BatteryHealth(double Percent, double FullWh, double DesignWh, double RateW);

/// <summary>
/// The charge history behind the battery panel's chart — battery-history.csv in the data folder, one line per
/// change plus one every 10 minutes while LapDock runs, kept for 7 days — and battery health: today's
/// full-charge capacity against the design capacity, the same numbers as Windows' battery report.
/// </summary>
public static class BatteryHistory
{
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";
    private static readonly string FilePath = Path.Combine(SettingsService.Dir, "battery-history.csv");

    public static void Record(BatteryInfo b)
    {
        if (!b.Present) return;
        try
        {
            Directory.CreateDirectory(SettingsService.Dir);
            File.AppendAllText(FilePath, Line(new BatterySample(DateTime.Now, b.Percent, b.PluggedIn)) + "\n");
        }
        catch (Exception) { /* history is a nicety */ }
    }

    /// <summary>At startup: drop what's older than a week.</summary>
    public static void Prune()
    {
        try
        {
            if (File.Exists(FilePath)) File.WriteAllLines(FilePath, Load(DateTime.Now.AddDays(-7)).Select(Line));
        }
        catch (Exception) { }
    }

    public static List<BatterySample> Load(DateTime since)
    {
        try { return File.ReadLines(FilePath).Select(Parse).OfType<BatterySample>().Where(s => s.At >= since).ToList(); }
        catch (Exception) { return new(); }
    }

    public static string Line(BatterySample s) =>
        $"{s.At.ToString(TimeFormat, CultureInfo.InvariantCulture)},{s.Percent},{(s.PluggedIn ? 1 : 0)}";

    public static BatterySample? Parse(string line)
    {
        var p = line.Split(',');
        return p.Length == 3 && DateTime.TryParseExact(p[0], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
               && int.TryParse(p[1], out var percent)
            ? new BatterySample(at, percent, p[2] == "1")
            : null;
    }

    /// <summary>The last <paramref name="count"/> slots up to now: each slot's last reading (null = the PC was off or asleep).</summary>
    public static (int Percent, bool PluggedIn)?[] Slots(IEnumerable<BatterySample> samples, DateTime now, int count = 48, double slotMinutes = 30)
    {
        var slots = new (int, bool)?[count];
        var start = now.AddMinutes(-count * slotMinutes);
        foreach (var s in samples.OrderBy(s => s.At))
        {
            int i = (int)Math.Floor((s.At - start).TotalMinutes / slotMinutes);
            if (i >= 0 && i < count) slots[i] = (s.Percent, s.PluggedIn);
        }
        return slots;
    }

    public static BatteryHealth? Health()
    {
        try
        {
            var r = Battery.AggregateBattery.GetReport();
            if (r.DesignCapacityInMilliwattHours is not > 0 || r.FullChargeCapacityInMilliwattHours is not > 0) return null;
            double design = r.DesignCapacityInMilliwattHours.Value, full = r.FullChargeCapacityInMilliwattHours.Value;
            return new BatteryHealth(Math.Min(100, 100 * full / design), full / 1000, design / 1000, (r.ChargeRateInMilliwatts ?? 0) / 1000.0);
        }
        catch (Exception) { return null; } // no battery, or a driver that doesn't report capacities
    }

    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        var s = new BatterySample(new DateTime(2026, 10, 13, 21, 5, 0), 57, true);
        Debug.Assert(Parse(Line(s)) == s);
        Debug.Assert(Parse("garbage") is null && Parse("2026-10-13T21:05:00,x,1") is null);
        var now = new DateTime(2026, 10, 13, 22, 0, 0);
        var slots = Slots(new[] { s, s with { At = now.AddMinutes(-1), Percent = 80, PluggedIn = false }, s with { At = now.AddDays(-2) } }, now);
        Debug.Assert(slots[^1] == (80, false));                    // the last minute: the newest slot
        Debug.Assert(slots[^2] == (57, true));                     // 21:05 is in 21:00–21:30
        Debug.Assert(slots.Count(x => x is not null) == 2);        // two days ago is off the chart
    }
}
