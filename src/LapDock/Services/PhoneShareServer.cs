using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace LapDock.Services;

/// <summary>
/// "Scan to download": while a file's QR code is on screen, a phone on the same Wi-Fi can fetch that one file.
/// A minimal HTTP server on this PC's network address; the link carries a random 128-bit token and anything
/// else gets a 404. It stops when the QR code is closed, or after 10 minutes. The first time, Windows Firewall
/// may ask whether LapDock may receive connections on this network.
/// </summary>
public sealed class PhoneShareServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _file, _token;
    private readonly CancellationTokenSource _stop = new();

    public string Url { get; }

    private PhoneShareServer(string file, IPAddress address)
    {
        _file = file;
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _listener = new TcpListener(address, 0);
        _listener.Start(8);
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"http://{address}:{port}/{_token}/{Uri.EscapeDataString(Path.GetFileName(file))}";
        _stop.CancelAfter(TimeSpan.FromMinutes(10));
        _stop.Token.Register(() => { try { _listener.Stop(); } catch (Exception) { } });
        _ = Task.Run(AcceptAsync); // network work stays off the UI thread
    }

    /// <summary>null when this PC isn't on a network (nothing could reach it).</summary>
    public static PhoneShareServer? Start(string file, IPAddress? address = null)
    {
        address ??= NetworkAddress();
        return address is null ? null : new PhoneShareServer(file, address);
    }

    /// <summary>The address other devices on the network reach this PC at (the interface that routes outward; nothing is sent).</summary>
    private static IPAddress? NetworkAddress()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect("8.8.8.8", 65530);
            return (probe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (Exception) { return null; }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
                _ = ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token));
        }
        catch (Exception) { /* stopped */ }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10)); // for the request; the download itself may take longer
                var stream = client.GetStream();
                var request = (await ReadHeadAsync(stream, timeout.Token)).Split(' ');
                bool get = request is ["GET", _, ..], head = request is ["HEAD", _, ..];
                if (!(get || head) || !request[1].StartsWith($"/{_token}/", StringComparison.Ordinal))
                {
                    await WriteAsync(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", _stop.Token);
                    return;
                }
                await using var file = File.OpenRead(_file);
                var name = Path.GetFileName(_file);
                var ascii = new string(name.Select(c => c is < ' ' or > '~' or '"' or '\\' ? '_' : c).ToArray());
                await WriteAsync(stream,
                    "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                    $"Content-Length: {file.Length}\r\n" +
                    $"Content-Disposition: attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}\r\n" +
                    "Cache-Control: no-store\r\nConnection: close\r\n\r\n", _stop.Token);
                if (get) await file.CopyToAsync(stream, _stop.Token);
            }
            catch (Exception) { /* the phone went away, the file moved, or it was stopped */ }
        }
    }

    /// <summary>The request line ("GET /path HTTP/1.1"), after reading the whole head (at most 8 KB).</summary>
    private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[8192];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), token);
            if (read == 0) break;
            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n")) break;
        }
        var head = Encoding.ASCII.GetString(buffer, 0, length);
        int end = head.IndexOf("\r\n", StringComparison.Ordinal);
        return end < 0 ? "" : head[..end];
    }

    private static Task WriteAsync(NetworkStream stream, string text, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), token).AsTask();

    public void Dispose() => _stop.Cancel();

    /// <summary>Serves a file on this PC only (loopback, no firewall involved): the right bytes for the link, 404 otherwise.</summary>
    [Conditional("DEBUG")]
    public static void SelfCheck()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapdock-share-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "hello phone");
        try
        {
            using var server = Start(path, IPAddress.Loopback)!;
            using var http = new HttpClient();
            var ok = http.GetAsync(server.Url).GetAwaiter().GetResult();
            Debug.Assert(ok.IsSuccessStatusCode && ok.Content.ReadAsStringAsync().GetAwaiter().GetResult() == "hello phone");
            var wrong = http.GetAsync(server.Url.Replace(server._token, new string('0', 32))).GetAwaiter().GetResult();
            Debug.Assert((int)wrong.StatusCode == 404);
        }
        finally { File.Delete(path); }
    }
}
