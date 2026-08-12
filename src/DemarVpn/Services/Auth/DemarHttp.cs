using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DemarVpn.Services.Auth;

public sealed class AuthException(string message, int statusCode = 0, int? retryAfterSeconds = null, string? hint = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
    public string? Hint { get; } = hint;
    public bool IsSessionInvalid => StatusCode == 401;
}

/// <summary>
/// Control-plane HTTPS. Origin A for api/fastapi/account is DPI-broken on some ISPs;
/// resolve those hosts to Cloudflare anycast (same A as demar.cc) so SNI/Host stay correct.
/// </summary>
public static class DemarHttp
{
    public const string BaseUrl = "https://auth.example.invalid";
    public const string VpnApiBaseUrl = "https://vpn.example.invalid";
    public const string AccountBaseUrl = "https://account.example.invalid";
    public const string ClientId = "demar-vpn";
    public const string AppOauthReturn = $"{AccountBaseUrl}/auth/app/complete";

    private static readonly HashSet<string> ControlHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth.example.invalid", "vpn.example.invalid", "account.example.invalid",
    };

    private const string EdgeBootstrap = "demar.cc";
    private static readonly string[] CfFallback = ["188.114.97.1", "188.114.96.1"];
    private static readonly TimeSpan EdgeTtl = TimeSpan.FromMinutes(5);

    private const int ConnectTimeoutMs = 4_000;
    private const int ReadTimeoutMs = 8_000;
    private const int DefaultAttempts = 3;

    private static readonly object EdgeGate = new();
    private static (DateTimeOffset At, IPAddress[] Ips)? _edgeCache;
    private static readonly AsyncLocal<int> EdgeIndex = new();
    private static readonly HttpClient Http = CreateClient(
        CreateControlHandler(ConnectTimeoutMs), ConnectTimeoutMs + ReadTimeoutMs);

    /// <summary>e.g. DESKTOP-ABC (user) - DemarVPN (1.0.9) (Windows). ASCII only — HttpClient rejects non-ASCII UA.</summary>
    public static string UserAgent
    {
        get
        {
            var pc = SanitizeToken(Environment.MachineName, "PC");
            var user = SanitizeToken(Environment.UserName, "user");
            var s = $"{pc} ({user}) - DemarVPN ({Strings.VersionName}) (Windows)";
            return s.Length <= 120 ? s : s[..120];
        }
    }

    public static bool IsControlHost(string host) => ControlHosts.Contains(host);

    public static void ClearEdgeCacheForTest()
    {
        lock (EdgeGate) _edgeCache = null;
    }

    /// <summary>Shared CF-edge dial for control hosts (SNI stays on request hostname).</summary>
    public static SocketsHttpHandler CreateControlHandler(int connectTimeoutMs = ConnectTimeoutMs) =>
        new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromMilliseconds(connectTimeoutMs),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectViaEdgeAsync,
        };

    private static string SanitizeToken(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '_' or '-')
                sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : fallback;
    }

    private static HttpClient CreateClient(SocketsHttpHandler handler, int timeoutMs)
    {
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Demar-Client", ClientId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return client;
    }

    private static async ValueTask<Stream> ConnectViaEdgeAsync(
        SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var port = ctx.DnsEndPoint.Port;
        Socket socket;
        if (IsControlHost(host))
        {
            var edges = EdgeIps();
            var edge = edges[EdgeIndex.Value % edges.Length];
            socket = new Socket(edge.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(new IPEndPoint(edge, port), ct).ConfigureAwait(false); }
            catch { socket.Dispose(); throw; }
        }
        else
        {
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(ctx.DnsEndPoint, ct).ConfigureAwait(false); }
            catch { socket.Dispose(); throw; }
        }
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static IPAddress[] EdgeIps()
    {
        lock (EdgeGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_edgeCache is { } cached && now - cached.At < EdgeTtl && cached.Ips.Length > 0)
                return cached.Ips;

            IPAddress[] ips;
            try
            {
                ips = Dns.GetHostAddresses(EdgeBootstrap)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .ToArray();
            }
            catch { ips = []; }

            if (ips.Length == 0)
            {
                ips = CfFallback
                    .Select(s => IPAddress.TryParse(s, out var a) ? a : null)
                    .OfType<IPAddress>()
                    .ToArray();
            }

            if (ips.Length == 0)
                throw new HttpRequestException($"No Cloudflare edge for {EdgeBootstrap}");

            _edgeCache = (now, ips);
            return ips;
        }
    }

    public static Task<JsonObject> PostJsonAsync(string url, JsonObject body, CancellationToken ct = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            Version = HttpVersion.Version11,
        }, ct);

    public static Task<JsonObject> GetJsonAsync(string url, string? bearer = null, CancellationToken ct = default) =>
        SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url) { Version = HttpVersion.Version11 };
            if (!string.IsNullOrEmpty(bearer))
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
            return req;
        }, ct);

    private static async Task<JsonObject> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        // Mirror Android Dispatchers.IO — never block WinUI UI thread / sync context.
        return await Task.Run(async () =>
        {
            Exception? last = null;
            for (var i = 0; i < DefaultAttempts; i++)
            {
                EdgeIndex.Value = i;
                using var req = createRequest();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(ConnectTimeoutMs + ReadTimeoutMs);
                if (i == 0)
                    AppLog.Append($"auth http: {req.Method} {req.RequestUri}");
                try
                {
                    using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                        .ConfigureAwait(false);
                    var raw = await res.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                    var json = ParseObject(raw);
                    if (!res.IsSuccessStatusCode)
                    {
                        var message = OptString(json, "error") ?? $"HTTP {(int)res.StatusCode}";
                        var retry = OptInt(json, "retryAfterSeconds");
                        var hint = OptString(json, "hint");
                        AppLog.Append($"auth http: {(int)res.StatusCode} {message} hint={hint}");
                        throw new AuthException(message, (int)res.StatusCode, retry > 0 ? retry : null, hint);
                    }
                    AppLog.Append($"auth http: {(int)res.StatusCode} ok");
                    return json;
                }
                catch (AuthException) { throw; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException)
                {
                    last = new AuthException(Strings.LoginErrorNetwork);
                }
                catch (HttpRequestException ex)
                {
                    last = ex;
                }
                catch (Exception ex)
                {
                    AppLog.Append($"auth http: failed — {ex.GetType().Name}: {ex.Message}");
                    throw new AuthException(Strings.LoginErrorGeneric);
                }

                if (i < DefaultAttempts - 1)
                    AppLog.Append($"http: retry {i + 1}/{DefaultAttempts} edge={EdgeLabel(i)} — {last?.Message}");
            }

            AppLog.Append($"auth http: network — {last?.Message}");
            throw last is AuthException ae ? ae : new AuthException(Strings.LoginErrorNetwork);
        }, ct).ConfigureAwait(true);
    }

    private static string EdgeLabel(int attempt)
    {
        try
        {
            var edges = EdgeIps();
            return edges[attempt % edges.Length].ToString();
        }
        catch { return "?"; }
    }

    public static JsonObject ParseObject(string raw)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject
                   ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    public static string? OptString(JsonObject json, string key)
    {
        if (!json.TryGetPropertyValue(key, out var n) || n is null || n.GetValueKind() == JsonValueKind.Null)
            return null;
        var s = n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString();
        return string.IsNullOrWhiteSpace(s) || s == "null" ? null : s.Trim('"');
    }

    public static int OptInt(JsonObject json, string key)
    {
        if (!json.TryGetPropertyValue(key, out var n) || n is null) return -1;
        try { return n.GetValue<int>(); }
        catch { return -1; }
    }

    public static bool OptBool(JsonObject json, string key) => OptBool(json, key, false);

    public static bool OptBool(JsonObject json, string key, bool defaultValue)
    {
        if (!json.TryGetPropertyValue(key, out var n) || n is null || n.GetValueKind() == JsonValueKind.Null)
            return defaultValue;
        try { return n.GetValue<bool>(); }
        catch { return defaultValue; }
    }
}
