using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace LapDock.Services;

/// <summary>
/// Internet for the Stats tab: live throughput (adapter byte counters), ping, and a download
/// speed test against a plain file URL — no account, no API key.
/// </summary>
public sealed class NetworkService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private (long Down, long Up, TimeSpan At)? _last;

    /// <summary>Bytes per second since the previous call (0, 0 on the first).</summary>
    public (double Down, double Up) Throughput()
    {
        long down = 0, up = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = nic.GetIPStatistics();
                down += s.BytesReceived;
                up += s.BytesSent;
            }
        }
        catch (NetworkInformationException) { return (0, 0); }

        var now = _clock.Elapsed;
        var previous = _last;
        _last = (down, up, now);
        if (previous is not { } p) return (0, 0);
        double seconds = (now - p.At).TotalSeconds;
        // An adapter disappearing makes the totals drop: report 0, not a negative rate.
        return seconds <= 0 ? (0, 0) : (Math.Max(0, down - p.Down) / seconds, Math.Max(0, up - p.Up) / seconds);
    }

    /// <summary>Round trip in ms, or null when the host doesn't answer.</summary>
    public static async Task<long?> PingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1500);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Downloads <paramref name="url"/> and returns megabits per second. Timing starts at the first byte,
    /// so connection setup (shown as "Finding a server…") doesn't drag the result down.
    /// </summary>
    public static async Task<double> DownloadMbpsAsync(string url, Action firstByte, Action<double> progress)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[81920];
        long total = 0;
        Stopwatch? timer = null;
        var lastReport = TimeSpan.Zero;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (timer is null) { timer = Stopwatch.StartNew(); firstByte(); continue; } // first chunk only starts the clock
            total += read;
            if (timer.Elapsed - lastReport > TimeSpan.FromMilliseconds(250))
            {
                lastReport = timer.Elapsed;
                progress(Mbps(total, timer.Elapsed));
            }
        }
        if (timer is null || total == 0) throw new IOException("The test file was empty.");
        return Mbps(total, timer.Elapsed);
    }

    public static double Mbps(long bytes, TimeSpan elapsed) =>
        elapsed.TotalSeconds <= 0 ? 0 : bytes * 8 / elapsed.TotalSeconds / 1_000_000;

    /// <summary>"820 KB/s", "12.4 MB/s".</summary>
    public static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond >= 1_000_000 ? $"{bytesPerSecond / 1_000_000:0.0} MB/s" : $"{bytesPerSecond / 1_000:0} KB/s";

    /// <summary>Debug-build self-check of the speed math (runs at startup, see App.xaml.cs).</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        Debug.Assert(Math.Abs(Mbps(12_500_000, TimeSpan.FromSeconds(1)) - 100) < 1e-9); // 12.5 MB in 1 s = 100 Mbps
        Debug.Assert(Mbps(1, TimeSpan.Zero) == 0);
        Debug.Assert(FormatRate(820_000) == "820 KB/s" && FormatRate(12_400_000) == $"{12.4:0.0} MB/s");
    }
}
