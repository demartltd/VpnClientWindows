using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

public enum TunnelReadyResult { Ok, TcpFail, HttpFail }

public static class VpnTunnelProbe
{
    private const string ProbeUrl = "https://www.cloudflare.com/cdn-cgi/trace";
    private const int Attempts = 5;
    private const int RetryDelayMs = 500;
    private const int ReadyPollMs = 200;
    private const int NaiveWarmupMs = 2000;
    private const int TcpProbeMs = 1000;
    private static readonly HttpClient Http = CreateHttpClient();

    public static TunnelReadyResult WaitForTunnelReady(int budgetMs)
    {
        if (budgetMs <= 0) return TunnelReadyResult.TcpFail;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
        if (!WaitForDiagProxy((int)Math.Min(budgetMs, 10_000))) return TunnelReadyResult.TcpFail;
        var remaining = (deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
        if (remaining <= 0) return TunnelReadyResult.HttpFail;
        Thread.Sleep((int)Math.Min(NaiveWarmupMs, remaining));
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (HttpOk()) return TunnelReadyResult.Ok;
            if (attempt < Attempts - 1)
            {
                remaining = (deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
                if (remaining <= 0) return TunnelReadyResult.HttpFail;
                Thread.Sleep((int)Math.Min(RetryDelayMs, remaining));
            }
        }
        return TunnelReadyResult.HttpFail;
    }

    private static bool WaitForDiagProxy(int budgetMs)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (TcpOpen(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort)) return true;
            Thread.Sleep(ReadyPollMs);
        }
        return false;
    }

    private static bool HttpOk()
    {
        var body = HttpGetViaDiagProxy(ProbeUrl);
        return body is not null && body.Contains("ip=");
    }

    private static bool TcpOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port);
            if (!connect.Wait(TcpProbeMs)) return false;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string? HttpGetViaDiagProxy(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TcpProbeMs * 3);
            var body = Http.GetStringAsync(url, cts.Token).GetAwaiter().GetResult();
            if (body.Contains("ip=")) return body;
            return string.IsNullOrWhiteSpace(body) ? "ip=0.0.0.0" : body;
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
