using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

public static class TunnelPathProbe
{
    public const string TraceIpUrl = "https://1.1.1.1/cdn-cgi/trace";

    private const int HttpTimeoutMs = 3_600;

    private static readonly HttpClient DiagHttp = CreateDiagClient();

    public static HttpClient DiagClient => DiagHttp;

    public static async Task<int> Tcp443ViaDiagAsync(string host, CancellationToken cancellationToken = default)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HttpTimeoutMs);
            await client.ConnectAsync(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, cts.Token);
            await using var stream = client.GetStream();
            var req = System.Text.Encoding.ASCII.GetBytes($"CONNECT {host}:443 HTTP/1.1\r\nHost: {host}:443\r\n\r\n");
            await stream.WriteAsync(req, cts.Token);
            var buf = new byte[64];
            var n = await stream.ReadAsync(buf, cts.Token);
            var status = System.Text.Encoding.ASCII.GetString(buf, 0, n);
            return status.Contains(" 200") ? (int)sw.ElapsedMilliseconds : 0;
        }
        catch
        {
            return 0;
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
        return new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs) };
    }
}
