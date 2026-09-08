using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

public readonly record struct TunnelProbeResult(int ElapsedMs, string? Detail)
{
    public bool Ok => ElapsedMs > 0;
}

public static class TunnelPathProbe
{
    public const string TraceIpUrl = "https://1.1.1.1/cdn-cgi/trace";

    private static readonly HttpClient DiagHttp = CreateDiagClient();

    public static HttpClient DiagClient => DiagHttp;

    /// <summary>Poll CONNECT via diag until success or <see cref="ConnectTimeouts.ProbeMs"/>.</summary>
    public static async Task<TunnelProbeResult> Tcp443ViaDiagAsync(
        string host, CancellationToken cancellationToken = default)
    {
        string? last = null;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ConnectTimeouts.ProbeMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = ConnectTimeouts.ProbeMs - (int)sw.ElapsedMilliseconds;
            if (left < 150) break;

            var attempt = await TryConnectOnceAsync(
                host, Math.Min(ConnectTimeouts.ProbeAttemptMs, left), cancellationToken);
            if (attempt.Ok)
                return new TunnelProbeResult((int)sw.ElapsedMilliseconds, null);

            last = attempt.Detail;
            await Task.Delay(120, cancellationToken);
        }

        return new TunnelProbeResult(0, last ?? $"timeout after {ConnectTimeouts.ProbeMs}ms");
    }

    private static async Task<TunnelProbeResult> TryConnectOnceAsync(
        string host, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            await client.ConnectAsync(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, cts.Token);
            await using var stream = client.GetStream();
            var req = System.Text.Encoding.ASCII.GetBytes(
                $"CONNECT {host}:443 HTTP/1.1\r\nHost: {host}:443\r\n\r\n");
            await stream.WriteAsync(req, cts.Token);
            var buf = new byte[128];
            var n = await stream.ReadAsync(buf, cts.Token);
            if (n <= 0)
                return new TunnelProbeResult(0, "empty CONNECT response");
            var status = System.Text.Encoding.ASCII.GetString(buf, 0, n).Trim();
            var line = status.Split('\n')[0].Trim();
            if (line.Contains(" 200"))
                return new TunnelProbeResult((int)sw.ElapsedMilliseconds, null);
            return new TunnelProbeResult(0, $"CONNECT {line}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TunnelProbeResult(0, $"CONNECT timeout {timeoutMs}ms");
        }
        catch (Exception ex)
        {
            return new TunnelProbeResult(0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static HttpClient CreateDiagClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            Proxy = new WebProxy(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort),
            UseProxy = true,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(ConnectTimeouts.ProbeMs) };
    }
}
