using System.Net.Http;
using System.Text.Json;

namespace LapDock.Services;

public readonly record struct WeatherInfo(double TempC, int Code, string Summary);
public readonly record struct Place(string Name, string Country, double Latitude, double Longitude);

/// <summary>Current weather from Open-Meteo — free, no API key, no signup.</summary>
public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<WeatherInfo?> GetCurrentAsync(double lat, double lon)
    {
        try
        {
            // Invariant: a comma-decimal locale would otherwise send "latitude=34,1688".
            var url = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            double temp = cur.GetProperty("temperature_2m").GetDouble();
            int code = cur.GetProperty("weather_code").GetInt32();
            return new WeatherInfo(temp, code, Describe(code));
        }
        catch { return null; } // offline is fine — the widget just shows "—"
    }

    /// <summary>A city name → coordinates (Open-Meteo geocoding, no key). Null when nothing matches or offline.</summary>
    public static async Task<Place?> FindPlaceAsync(string name)
    {
        try
        {
            var url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(name);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
            var r = results[0];
            return new Place(r.GetProperty("name").GetString() ?? name,
                r.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "",
                r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
        }
        catch { return null; }
    }

    private static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 or 2 => "Mostly clear",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunder & hail",
        _ => "—"
    };
}
