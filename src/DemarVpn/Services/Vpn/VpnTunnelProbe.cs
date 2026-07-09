using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

public enum TunnelReadyResult { Ok, TcpFail, HttpFail }

public static class VpnTunnelProbe
{
    public const string ProbeHost = "www.cloudflare.com";
    public const string FallbackHostGoogle = "www.google.com";
    public static readonly IReadOnlyList<string> ProbeBootstrapDomains = [ProbeHost, FallbackHostGoogle];

    private static readonly string[] ProbeUrls =
    [
        $"https://{ProbeHost}/cdn-cgi/trace",
        $"https://{FallbackHostGoogle}/generate_204",
    ];

    private const int Attempts = 3;
    private const int RetryDelayMs = 300;
    private const int ReadyPollMs = 200;
    private const int TcpProbeMs = 800;
    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<TunnelReadyResult> WaitForTunnelReadyAsync(int budgetMs, CancellationToken cancellationToken = default)
    {
        if (budgetMs <= 0) return TunnelReadyResult.TcpFail;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
        if (!await WaitForDiagProxyAsync((int)Math.Min(budgetMs, 10_000), cancellationToken))
            return TunnelReadyResult.TcpFail;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await HttpOkAsync(cancellationToken))
                return TunnelReadyResult.Ok;
            if (attempt < Attempts - 1)
            {
                var remaining = (deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
                if (remaining <= 0) return TunnelReadyResult.HttpFail;
                await Task.Delay((int)Math.Min(RetryDelayMs, remaining), cancellationToken);
            }
        }
        return TunnelReadyResult.HttpFail;
    }

    private static async Task<bool> WaitForDiagProxyAsync(int budgetMs, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TcpOpenAsync(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, cancellationToken))
                return true;
            await Task.Delay(ReadyPollMs, cancellationToken);
        }
        return false;
    }

    private static async Task<bool> HttpOkAsync(CancellationToken cancellationToken)
    {
        foreach (var url in ProbeUrls)
        {
            var body = await HttpGetViaDiagProxyAsync(url, cancellationToken);
            if (body is null) continue;
            if (url.Contains("cdn-cgi/trace") && body.Contains("ip=")) return true;
            if (url.Contains("generate_204")) return true;
        }
        return false;
    }

    private static async Task<bool> TcpOpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TcpProbeMs);
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> HttpGetViaDiagProxyAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TcpProbeMs * 3);
            var body = await Http.GetStringAsync(url, cts.Token);
            if (url.Contains("cdn-cgi/trace") && body.Contains("ip=")) return body;
            if (url.Contains("generate_204")) return string.IsNullOrWhiteSpace(body) ? "ok" : body;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort),
            UseProxy = true,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TcpProbeMs * 3) };
    }
}
