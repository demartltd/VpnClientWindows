using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Device;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace DemarVpn.Services.Support;

/// <summary>Customer Support API — parity Android/Apple SupportApi.</summary>
public static class SupportApi
{
    public static string BaseUrl => DemarEnv.SupportApiBase;
    public const string VpnSourceId = "2";
    public const string Channel = "windows";
    public const long MaxMediaBytes = 25L * 1024 * 1024;
    public const int MaxMediaFiles = 10;

    private const int ConnectTimeoutMs = 15_000;
    private const int ReadTimeoutMs = 25_000;
    private const int MediaTimeoutMs = 180_000;
    private const int MaxPhotoEdge = 1024;
    private const int MaxPhotoBytes = 400 * 1024;

    private static readonly HttpClient Http = CreateClient(ConnectTimeoutMs + ReadTimeoutMs);
    private static readonly HttpClient MediaHttp = CreateClient(MediaTimeoutMs);

    private static HttpClient CreateClient(int timeoutMs)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            BaseAddress = new Uri(BaseUrl),
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Demar-Client", DemarHttp.ClientId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DemarHttp.UserAgent);
        return client;
    }

    public static Task<IReadOnlyList<SupportTicketSummary>> ListTicketsAsync(
        string userId, string loginAt, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var json = await GetAsync("/support/me", userId, loginAt, null, ct).ConfigureAwait(false);
            return SupportJson.ParseSummaries(json["tickets"] as JsonArray);
        }, ct);

    public static Task<IReadOnlyList<SupportTheme>> ListThemesAsync(
        string userId, string loginAt, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var json = await GetAsync("/support/themes", userId, loginAt,
                new Dictionary<string, string> { ["sourceId"] = VpnSourceId }, ct).ConfigureAwait(false);
            return SupportJson.ParseThemes(json["themes"] as JsonArray);
        }, ct);

    public static Task<SupportTicket> GetTicketAsync(
        string userId, string loginAt, string ticketId, CancellationToken ct = default) =>
        Task.Run(async () =>
            SupportJson.ParseTicketPayload(await GetAsync($"/support/tickets/{Enc(ticketId)}", userId, loginAt, null, ct)
                .ConfigureAwait(false)), ct);

    public static Task<SupportTicket> MarkTicketReadAsync(
        string userId, string loginAt, string ticketId, CancellationToken ct = default) =>
        Task.Run(async () =>
            SupportJson.ParseTicketPayload(await PostAsync($"/support/tickets/{Enc(ticketId)}/read",
                SessionBody(userId, loginAt), hasMedia: false, ct).ConfigureAwait(false)), ct);

    public static Task<SupportTicket> PostTicketAsync(
        string userId,
        string loginAt,
        string? text = null,
        IReadOnlyList<SupportUploadMedia>? media = null,
        string? log = null,
        IReadOnlyList<SupportUploadMedia>? logMedia = null,
        string? themeId = null,
        string? ticketId = null,
        bool forceNew = false,
        CancellationToken ct = default)
    {
        media ??= [];
        logMedia ??= [];
        return Task.Run(async () =>
        {
            var body = SessionBody(userId, loginAt);
            body["channel"] = Channel;
            if (forceNew) body["forceNew"] = true;
            if (!string.IsNullOrWhiteSpace(ticketId)) body["ticketId"] = ticketId;
            if (!string.IsNullOrWhiteSpace(text)) body["text"] = text;
            PutLog(body, log, logMedia);
            if (!string.IsNullOrWhiteSpace(themeId)) body["themeId"] = themeId;
            body["sourceId"] = VpnSourceId;
            if (media.Count > 0) body["media"] = MediaToJson(media);
            var hasMedia = media.Count > 0 || logMedia.Count > 0;
            return SupportJson.ParseTicketPayload(await PostAsync("/support/tickets", body, hasMedia, ct).ConfigureAwait(false));
        }, ct);
    }

    /// <summary>Create ticket with silent diagnostics: device text + full journal.txt attach.</summary>
    public static Task<SupportTicket> PostTicketCreateAsync(
        string userId,
        string loginAt,
        string? text = null,
        string? log = null,
        IReadOnlyList<SupportUploadMedia>? logMedia = null,
        string? themeId = null,
        bool forceNew = false,
        CancellationToken ct = default) =>
        PostTicketWithRetryAsync(
            userId, loginAt, text, [], log, logMedia ?? [], themeId, null, forceNew, ct, maxAttempts: 2);

    private static async Task<SupportTicket> PostTicketWithRetryAsync(
        string userId, string loginAt, string? text, IReadOnlyList<SupportUploadMedia> media,
        string? log, IReadOnlyList<SupportUploadMedia> logMedia, string? themeId, string? ticketId,
        bool forceNew, CancellationToken ct, int maxAttempts = 3)
    {
        var delayMs = 400;
        Exception last = new AuthException("ticket post failed", 500);
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await PostTicketAsync(
                    userId, loginAt, text, media, log, logMedia, themeId, ticketId, forceNew, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                var msg = ex.Message ?? "";
                AppLog.Append($"support ticket post try{attempt + 1}: {msg}");
                if (attempt == maxAttempts - 1 || !IsRetryableRelay(ex)) throw;
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                delayMs *= 2;
            }
        }
        throw last;
    }

    private static bool IsRetryableRelay(Exception ex)
    {
        var msg = ex.Message ?? "";
        return msg.Contains("fetch failed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("архив", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("archive", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("сохранить лог", StringComparison.OrdinalIgnoreCase)
            || (ex is AuthException ae && ae.StatusCode == 502);
    }

    /// <summary>Album: one POST with media[1…10] → one ticket message.</summary>
    public static Task<SupportTicket> PostMediaAlbumAsync(
        string userId, string loginAt, IReadOnlyList<SupportUploadMedia> media, string ticketId,
        CancellationToken ct = default)
    {
        if (media.Count == 0) throw new AuthException("media required", 400);
        AppLog.Append($"support media album: {media.Count} files");
        return PostTicketWithRetryAsync(
            userId, loginAt, text: null, media: media, log: null, logMedia: [],
            themeId: null, ticketId: ticketId, forceNew: false, ct, maxAttempts: 2);
    }

    public static Task<SupportTicket> PatchMessageAsync(
        string userId, string loginAt, string ticketId, string messageId,
        string? text = null, bool deleted = false, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var body = SessionBody(userId, loginAt);
            body["channel"] = Channel;
            if (deleted) body["deleted"] = true;
            else if (text is not null) body["text"] = text;
            else throw new AuthException("text or deleted required", 400);
            return SupportJson.ParseTicketPayload(await PatchAsync(
                $"/support/tickets/{Enc(ticketId)}/messages/{Enc(messageId)}", body, ct).ConfigureAwait(false));
        }, ct);

    public static async Task<SupportUploadMedia> ReadFileAsUploadAsync(string path, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new AuthException("cannot_read_file", 400);
        if (info.Length > MaxMediaBytes) throw new SupportMediaTooLargeException();
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        if (bytes.Length == 0) throw new AuthException("empty media", 400);
        if (bytes.Length > MaxMediaBytes) throw new SupportMediaTooLargeException();
        var webp = CompressImageToWebp(bytes)
                   ?? throw new AuthException("unsupported image", 400);
        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(stem)) stem = "photo";
        return new SupportUploadMedia(Convert.ToBase64String(webp), stem + ".webp", "image/webp");
    }

    public readonly record struct SilentDiagnostics(string LogText, IReadOnlyList<SupportUploadMedia> LogMedia);

    /// <summary>Device summary as log text; full journal as journal.txt attach.</summary>
    public static async Task<SilentDiagnostics> BuildSilentDiagnosticsAsync()
    {
        var device = await DeviceIdentity.LoadAsync().ConfigureAwait(false);
        string? latestVer = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var latest = await WindowsReleaseApi.FetchAsync(UpdateChannelStore.PreferBeta, cts.Token)
                .ConfigureAwait(false);
            latestVer = latest?.Version;
        }
        catch { /* optional */ }

        var ver = AssemblyVersion();
        var sb = new StringBuilder();
        sb.AppendLine($"Demar VPN {Strings.VersionName} ({ver})");
        sb.AppendLine($"Latest: {latestVer ?? "—"}");
        sb.AppendLine($"UA: {DemarHttp.UserAgent}");
        sb.AppendLine("--- device ---");
        foreach (var (k, v) in device) sb.AppendLine($"{k}={v}");
        var deviceText = sb.ToString().Trim();
        if (deviceText.Length > 8000) deviceText = deviceText[..8000];

        var logBody = AppLog.ReadPersistedUtf8();
        if (string.IsNullOrWhiteSpace(logBody)) logBody = "(empty)";
        var bytes = Encoding.UTF8.GetBytes(logBody);
        var file = new SupportUploadMedia(
            Convert.ToBase64String(bytes), "journal.txt", "text/plain");
        return new SilentDiagnostics(deviceText, new[] { file });
    }

    public static async Task<byte[]> LoadMediaBytesAsync(
        string userId, string loginAt, string mediaPath, string? ticketId = null,
        CancellationToken ct = default)
    {
        var path = NormalizeMediaPath(mediaPath);
        var cacheKey = $"{path}|{ticketId}|{userId}";
        var cached = SupportStore.LoadMedia(cacheKey);
        if (cached is not null) return cached;

        var sep = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var ticketQ = string.IsNullOrWhiteSpace(ticketId) ? "" : $"&ticketId={Enc(ticketId)}";
        var full = $"{BaseUrl}{path}{sep}userId={Enc(userId)}&loginAt={Enc(loginAt)}{ticketQ}";

        using var req = new HttpRequestMessage(HttpMethod.Get, full);
        req.Headers.Accept.Clear();
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        using var res = await MediaHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new AuthException(string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)res.StatusCode}" : err,
                (int)res.StatusCode);
        }
        var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        SupportStore.SaveMedia(cacheKey, bytes);
        return bytes;
    }

    public static string EncodeSummaries(IReadOnlyList<SupportTicketSummary> list) =>
        SupportJson.EncodeSummaries(list);

    public static IReadOnlyList<SupportTicketSummary>? DecodeSummaries(string raw) =>
        SupportJson.DecodeSummaries(raw);

    public static string EncodeTicket(SupportTicket ticket) => SupportJson.EncodeTicket(ticket);

    public static SupportTicket? DecodeTicket(string raw) => SupportJson.DecodeTicket(raw);

    public static SupportTicket ParseTicketPayload(JsonObject json) => SupportJson.ParseTicketPayload(json);

    public static IReadOnlyList<SupportTicketSummary> ParseSummaries(JsonArray? arr) =>
        SupportJson.ParseSummaries(arr);

    public static string? SupportErrorMessage(Exception t)
    {
        if (t is OperationCanceledException) return null;
        if (t is SupportMediaTooLargeException) return Strings.SupportMediaTooLarge;
        if (t is AuthException ae)
        {
            if (ae.Message.StartsWith("media: max", StringComparison.OrdinalIgnoreCase))
                return Strings.SupportMediaLimit;
            if (ae.Message == "ticket_channel_mismatch") return Strings.SupportChannelLocked;
            return string.IsNullOrWhiteSpace(ae.Message) ? Strings.SupportErrorGeneric : ae.Message;
        }
        return Strings.SupportErrorGeneric;
    }

    public static bool IsTransient(Exception t) =>
        t is AuthException { StatusCode: 502 }
        || t is HttpRequestException
        || t is TaskCanceledException;

    public static string? FormatMsgTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!DateTimeOffset.TryParse(raw.Trim().Replace(' ', 'T'), out var dto)) return null;
        return dto.ToLocalTime().ToString("HH:mm");
    }

    private static byte[]? CompressImageToWebp(byte[] raw)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(raw);
            var edge = Math.Max(image.Width, image.Height);
            if (edge > MaxPhotoEdge)
            {
                var scale = (double)MaxPhotoEdge / edge;
                var nw = Math.Max(1, (int)Math.Round(image.Width * scale));
                var nh = Math.Max(1, (int)Math.Round(image.Height * scale));
                image.Mutate(x => x.Resize(nw, nh));
            }

            var quality = 75;
            byte[]? best = null;
            while (true)
            {
                using var ms = new MemoryStream();
                image.Save(ms, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = quality,
                });
                best = ms.ToArray();
                if (best.Length <= MaxPhotoBytes || quality <= 25) break;
                quality -= 10;
            }
            return best is { Length: > 0 } ? best : null;
        }
        catch (Exception ex)
        {
            AppLog.Append($"support webp: {ex.Message}");
            return null;
        }
    }

    private static string NormalizeMediaPath(string mediaPath)
    {
        var path = mediaPath;
        if (path.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase))
            path = path[BaseUrl.Length..];
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out var u))
            path = u.PathAndQuery;
        if (!path.StartsWith('/')) path = "/" + path;
        if (path.StartsWith("/api/telegram-media?", StringComparison.Ordinal))
            path = "/support/media?" + path["/api/telegram-media?".Length..];
        return path;
    }

    private static JsonObject SessionBody(string userId, string loginAt) => new()
    {
        ["userId"] = userId,
        ["loginAt"] = loginAt,
    };

    private static JsonArray MediaToJson(IReadOnlyList<SupportUploadMedia> media)
    {
        var arr = new JsonArray();
        foreach (var m in media)
        {
            var item = new JsonObject { ["data"] = m.DataBase64 };
            if (!string.IsNullOrWhiteSpace(m.FileName)) item["fileName"] = m.FileName;
            if (!string.IsNullOrWhiteSpace(m.MimeType)) item["mimeType"] = m.MimeType;
            arr.Add(item);
        }
        return arr;
    }

    private static void PutLog(JsonObject body, string? log, IReadOnlyList<SupportUploadMedia> logMedia)
    {
        var text = string.IsNullOrWhiteSpace(log) ? null : log;
        if (logMedia.Count == 0)
        {
            if (text is not null) body["log"] = text;
            return;
        }
        var obj = new JsonObject();
        if (text is not null) obj["text"] = text;
        obj["media"] = MediaToJson(logMedia);
        body["log"] = obj;
    }

    private static string Enc(string v) => Uri.EscapeDataString(v);

    private static string AssemblyVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "—" : $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
    }

    private static async Task<JsonObject> GetAsync(
        string path, string userId, string loginAt, Dictionary<string, string>? extra, CancellationToken ct)
    {
        var q = new StringBuilder($"userId={Enc(userId)}&loginAt={Enc(loginAt)}");
        if (extra is not null)
            foreach (var (k, v) in extra) q.Append('&').Append(Enc(k)).Append('=').Append(Enc(v));
        return await SendAsync(HttpMethod.Get, $"{path}?{q}", null, false, ct).ConfigureAwait(false);
    }

    private static Task<JsonObject> PostAsync(string path, JsonObject body, bool hasMedia, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, body, hasMedia, ct);

    private static Task<JsonObject> PatchAsync(string path, JsonObject body, CancellationToken ct) =>
        SendAsync(HttpMethod.Patch, path, body, false, ct);

    private static async Task<JsonObject> SendAsync(
        HttpMethod method, string path, JsonObject? body, bool hasMedia, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null)
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var client = hasMedia ? MediaHttp : Http;
        try
        {
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            var raw = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var json = DemarHttp.ParseObject(raw);
            if (!res.IsSuccessStatusCode)
            {
                var err = DemarHttp.OptString(json, "error") ?? $"HTTP {(int)res.StatusCode}";
                AppLog.Append($"support http: {(int)res.StatusCode} {method} {path.Split('?')[0]} — {err}");
                if ((int)res.StatusCode == 413 || err.Contains("too large", StringComparison.OrdinalIgnoreCase))
                    throw new SupportMediaTooLargeException();
                if (err == "ticket_channel_mismatch")
                    throw new AuthException(err, 409);
                throw new AuthException(err, (int)res.StatusCode);
            }
            return json;
        }
        catch (SupportMediaTooLargeException) { throw; }
        catch (AuthException) { throw; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            AppLog.Append($"support http: timeout {method} {path.Split('?')[0]}");
            throw new AuthException(Strings.LoginErrorNetwork);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Append($"support http: network {method} {path.Split('?')[0]} — {ex.Message}");
            throw new AuthException(Strings.LoginErrorNetwork);
        }
    }
}

internal static partial class SupportUrl
{
    [GeneratedRegex(@"https?://[^\s<>\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();
}
