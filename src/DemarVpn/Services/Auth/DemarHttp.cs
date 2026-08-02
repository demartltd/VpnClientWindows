using System.Net;
using System.Net.Http.Headers;
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
}

/// <summary>Mobile API HTTP — parity with Android AuthApi (IO off UI, explicit timeouts).</summary>
public static class DemarHttp
{
    public const string BaseUrl = "https://auth.example.invalid";
    public const string VpnApiBaseUrl = "https://vpn.example.invalid";
    public const string AccountBaseUrl = "https://account.example.invalid";
    public const string ClientId = "demar-vpn";
    public const string AppOauthReturn = $"{AccountBaseUrl}/auth/app/complete";

    private const int ConnectTimeoutMs = 15_000;
    private const int ReadTimeoutMs = 25_000;

    private static readonly HttpClient Http = CreateClient();

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

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs + ReadTimeoutMs),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Demar-Client", ClientId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return client;
    }

    public static Task<JsonObject> PostJsonAsync(string url, JsonObject body, CancellationToken ct = default) =>
        SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                Version = HttpVersion.Version11,
            };
            return req;
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
            using var req = createRequest();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(ConnectTimeoutMs + ReadTimeoutMs);
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
            catch (AuthException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                AppLog.Append("auth http: timeout");
                throw new AuthException(Strings.LoginErrorNetwork);
            }
            catch (HttpRequestException ex)
            {
                AppLog.Append($"auth http: network — {ex.Message}");
                throw new AuthException(Strings.LoginErrorNetwork);
            }
            catch (Exception ex)
            {
                AppLog.Append($"auth http: failed — {ex.GetType().Name}: {ex.Message}");
                throw new AuthException(Strings.LoginErrorGeneric);
            }
        }, ct).ConfigureAwait(true);
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

    public static bool OptBool(JsonObject json, string key) =>
        json.TryGetPropertyValue(key, out var n) && n is not null && n.GetValueKind() != JsonValueKind.Null
        && n.GetValue<bool>();
}
