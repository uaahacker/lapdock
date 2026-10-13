using System.Net.Http;
using System.Text.Json;

namespace LapDock.Services;

public readonly record struct WeatherInfo(double TempC, int Code, string Summary);

/// <summary>Current weather from Open-Meteo — free, no API key, no signup.</summary>
public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<WeatherInfo?> GetCurrentAsync(double lat, double lon)
    {
        try
        {
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            double temp = cur.GetProperty("temperature_2m").GetDouble();
            int code = cur.GetProperty("weather_code").GetInt32();
            return new WeatherInfo(temp, code, Describe(code));
        }
        catch { return null; } // offline is fine — the widget just shows "—"
    }

    private static string Describe(int code) => code switch
    {
        0 => "☀️ Clear",
        1 or 2 => "🌤️ Mostly clear",
        3 => "☁️ Overcast",
        45 or 48 => "🌫️ Fog",
        51 or 53 or 55 => "🌦️ Drizzle",
        61 or 63 or 65 => "🌧️ Rain",
        66 or 67 => "🌧️ Freezing rain",
        71 or 73 or 75 or 77 => "❄️ Snow",
        80 or 81 or 82 => "🌧️ Showers",
        85 or 86 => "❄️ Snow showers",
        95 => "⛈️ Thunderstorm",
        96 or 99 => "⛈️ Thunder + hail",
        _ => "🌡️ —"
    };
}
