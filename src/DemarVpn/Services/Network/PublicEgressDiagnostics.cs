using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using DemarVpn.Domain.Vpn;
using DemarVpn.Models;
using DemarVpn.Services.Vpn;

namespace DemarVpn.Services.Network;

public enum EgressRouteKind { Direct, DemarVpn, OtherVpn }

public sealed record PublicEgressInfo(string Ip, string Country);

public sealed record EgressHostProbe(int TcpMs, int HttpsMs);

public static class PublicEgressDiagnostics
{
    public const int ProbeTimeoutMs = 2500;

    public static readonly PublicEgressInfo Fallback = new("localhost", "Unknown");
    public static readonly EgressHostProbe ZeroProbe = new(0, 0);

    public static readonly IReadOnlyList<string> ProbeHosts =
    [
        "github.com", "bing.com", "ya.ru", "vk.ru", "x.com", "bbc.com", "www.gov.cn",
    ];

    private static readonly HttpClient DirectHttp = CreateHttpClient(useTunnel: false);

    public static EgressRouteKind ClassifyRoute(VpnState state, EgressRouteSnapshot? snapshot = null)
    {
        var demar = state.Kind == VpnStateKind.Connected || snapshot?.DemarConnected == true;
        if (demar) return EgressRouteKind.DemarVpn;
        if (snapshot?.OsVpnActive == true) return EgressRouteKind.OtherVpn;
        return EgressRouteKind.Direct;
    }

    public static bool ShouldUseTunnel(EgressRouteKind route) => route == EgressRouteKind.DemarVpn;

    public static string RouteLabel(EgressRouteKind route) => route switch
    {
        EgressRouteKind.Direct => Strings.ServiceRouteDirect,
        EgressRouteKind.DemarVpn => Strings.ServiceRouteDemar,
        EgressRouteKind.OtherVpn => Strings.ServiceRouteOther,
        _ => "…",
    };

    public static string ProbeLabel(EgressHostProbe probe)
    {
        var t = probe.TcpMs > 0 ? probe.TcpMs.ToString() : "0";
        var h = probe.HttpsMs > 0 ? probe.HttpsMs.ToString() : "0";
        return $"TCP {t} · HTTPS {h}";
    }

    public static string CensorPublicIpv4(string ip)
    {
        if (ip is "localhost" or "127.0.0.1") return "localhost";
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"**.***.{parts[2]}.{parts[3]}" : "**.***";
    }

    public static async Task<EgressRouteKind> ResolveRouteAsync(VpnState state)
    {
        try
        {
            var snapshot = await WindowsEgressRouteSnapshot.CaptureAsync(state);
            return ClassifyRoute(state, snapshot);
        }
        catch
        {
            return ClassifyRoute(state);
        }
    }

    public static async Task<PublicEgressInfo> FetchGeoAsync(bool useTunnel, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(8_000);

            Func<CancellationToken, Task<PublicEgressInfo?>>[] fetches = useTunnel
                ? [ct => FetchCloudflareTraceAsync(useTunnel: true, ct)]
                :
                [
                    ct => FetchCloudflareTraceAsync(useTunnel: false, ct),
                    FetchIpWhoIsAsync,
                    FetchIpApiCoAsync,
                    FetchIpifyThenWhoIsAsync,
                ];

            return await FirstGeoAsync(fetches, cts.Token) ?? Fallback;
        }
        catch
        {
            return Fallback;
        }
    }

    public static PublicEgressInfo? ParseCloudflareTrace(string body)
    {
        string? ip = null;
        string? loc = null;
        foreach (var line in body.Split('\n'))
        {
            var i = line.IndexOf('=');
            if (i <= 0 || i >= line.Length - 1) continue;
            var key = line[..i].Trim();
            var value = line[(i + 1)..].Trim();
            if (key == "ip" && IsPublicIpv4(value)) ip = value;
            if (key == "loc" && value.Length == 2) loc = value.ToUpperInvariant();
        }
        if (ip is null) return null;
        return new PublicEgressInfo(ip, loc is null ? Fallback.Country : CountryName(loc));
    }

    public static string CountryName(string code) =>
        CountryNameOrNull(code) ?? code;

    internal static string? CountryNameOrNull(string code)
    {
        if (code.Length != 2) return null;
        try
        {
            return new RegionInfo(code.ToUpperInvariant()).EnglishName;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<EgressHostProbe> ProbeHostAsync(
        string host, bool useTunnel, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ProbeTimeoutMs);
            var probeTask = useTunnel
                ? ProbeViaTunnelAsync(host, cts.Token)
                : ProbeDirectAsync(host, cts.Token);
            var completed = await Task.WhenAny(probeTask, Task.Delay(ProbeTimeoutMs, cts.Token));
            return completed == probeTask ? await probeTask : ZeroProbe;
        }
        catch
        {
            return ZeroProbe;
        }
    }

    public static async Task<List<EgressHostProbe>> ProbeAllHostsAsync(
        bool useTunnel,
        Action<int, EgressHostProbe>? onHostReady = null,
        CancellationToken cancellationToken = default)
    {
        var results = new EgressHostProbe[ProbeHosts.Count];
        using var gate = new SemaphoreSlim(useTunnel ? 2 : 4);
        await Task.WhenAll(Enumerable.Range(0, ProbeHosts.Count).Select(async i =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var probe = await ProbeHostAsync(ProbeHosts[i], useTunnel, cancellationToken);
                results[i] = probe;
                onHostReady?.Invoke(i, probe);
            }
            finally
            {
                gate.Release();
            }
        }));
        return [.. results];
    }

    private static async Task<EgressHostProbe> ProbeDirectAsync(string host, CancellationToken cancellationToken)
    {
        var tcp = Tcp443Ms(host, cancellationToken);
        var https = HttpsMs(host, useTunnel: false, cancellationToken);
        await Task.WhenAll(tcp, https);
        return new(await tcp, await https);
    }

    private static async Task<EgressHostProbe> ProbeViaTunnelAsync(string host, CancellationToken cancellationToken)
    {
        var tcp = TunnelPathProbe.Tcp443ViaDiagAsync(host, cancellationToken);
        var https = HttpsMs(host, useTunnel: true, cancellationToken);
        await Task.WhenAll(tcp, https);
        return new(await tcp, await https);
    }

    private static async Task<int> Tcp443Ms(string host, CancellationToken cancellationToken)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(host, 443, cancellationToken);
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<int> HttpsMs(string host, bool useTunnel, CancellationToken cancellationToken)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var client = useTunnel ? TunnelPathProbe.DiagClient : DirectHttp;
            using var response = await client.GetAsync($"https://{host}/", cancellationToken);
            if ((int)response.StatusCode < 100) return 0;
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return 0;
        }
    }

    private static HttpClient CreateHttpClient(bool useTunnel)
    {
        if (useTunnel) return TunnelPathProbe.DiagClient;
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DemarVpn/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        return client;
    }

    /// <summary>Try providers one-by-one; next only after previous failed. Never parallel.</summary>
    private static async Task<PublicEgressInfo?> FirstGeoAsync(
        IReadOnlyList<Func<CancellationToken, Task<PublicEgressInfo?>>> fetches,
        CancellationToken cancellationToken)
    {
        foreach (var fetch in fetches)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var result = await fetch(cancellationToken).ConfigureAwait(false);
                if (result is not null) return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // try next provider
            }
        }
        return null;
    }

    private static async Task<PublicEgressInfo?> FetchCloudflareTraceAsync(bool useTunnel, CancellationToken cancellationToken)
    {
        var url = useTunnel ? TunnelPathProbe.TraceIpUrl : "https://www.cloudflare.com/cdn-cgi/trace";
        var client = useTunnel ? TunnelPathProbe.DiagClient : DirectHttp;
        var body = await HttpGetAsync(client, url, cancellationToken);
        return body is null ? null : ParseCloudflareTrace(body);
    }

    private static async Task<PublicEgressInfo?> FetchIpWhoIsAsync(CancellationToken cancellationToken)
    {
        var body = await HttpGetAsync(DirectHttp, "https://ipwho.is/", cancellationToken);
        return body is null ? null : ParseIpWhoIs(body);
    }

    private static async Task<PublicEgressInfo?> FetchIpApiCoAsync(CancellationToken cancellationToken)
    {
        var body = await HttpGetAsync(DirectHttp, "https://ipapi.co/json/", cancellationToken);
        return body is null ? null : ParseIpApiCo(body);
    }

    private static async Task<PublicEgressInfo?> FetchIpifyThenWhoIsAsync(CancellationToken cancellationToken)
    {
        var body = await HttpGetAsync(DirectHttp, "https://api.ipify.org?format=json", cancellationToken);
        if (body is null) return null;
        var ip = ParseIpify(body);
        if (ip is null) return null;
        var whois = await HttpGetAsync(DirectHttp, $"https://ipwho.is/{ip}", cancellationToken);
        return whois is null ? new PublicEgressInfo(ip, Fallback.Country) : ParseIpWhoIs(whois);
    }

    private static async Task<string?> HttpGetAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static PublicEgressInfo? ParseIpWhoIs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
            var ip = root.GetProperty("ip").GetString();
            if (string.IsNullOrWhiteSpace(ip) || !IsPublicIpv4(ip)) return null;
            var country = root.TryGetProperty("country", out var c) ? c.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(country))
            {
                var code = root.TryGetProperty("country_code", out var cc) ? cc.GetString()?.Trim() : null;
                country = code is null ? Fallback.Country : CountryName(code);
            }
            return new PublicEgressInfo(ip, country);
        }
        catch
        {
            return null;
        }
    }

    private static PublicEgressInfo? ParseIpApiCo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(ip) || !IsPublicIpv4(ip)) return null;
            var country = root.TryGetProperty("country_name", out var c) ? c.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(country))
            {
                var code = root.TryGetProperty("country_code", out var cc) ? cc.GetString()?.Trim() : null;
                country = code is null ? Fallback.Country : CountryName(code);
            }
            return new PublicEgressInfo(ip, country);
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseIpify(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var ip = doc.RootElement.GetProperty("ip").GetString();
            return string.IsNullOrWhiteSpace(ip) || !IsPublicIpv4(ip) ? null : ip;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPublicIpv4(string s)
    {
        var parts = s.Split('.');
        if (parts.Length != 4) return false;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var n) || n is < 0 or > 255) return false;
        }
        return true;
    }
}
