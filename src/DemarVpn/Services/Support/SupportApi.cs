using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Device;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DemarVpn.Services.Support;

/// <summary>Customer Support API — parity Android/Apple SupportApi (csauth.example.invalid).</summary>
public static class SupportApi
{
    public const string BaseUrl = "https://csauth.example.invalid";
    public const string VpnSourceId = "2";
    public const string Channel = "windows";
    public const long MaxMediaBytes = 25L * 1024 * 1024;
    public const int MaxMediaFiles = 10;

    private const int ConnectTimeoutMs = 15_000;
    private const int ReadTimeoutMs = 25_000;
    private const int MediaTimeoutMs = 180_000;
    private const int MaxPhotoEdge = 800;
    private const int MaxPhotoBytes = 200 * 1024;

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

    /// <summary>Create/reply with optional silent log. Retries CS relay failures; falls back to text-only log.</summary>
    public static async Task<SupportTicket> PostTicketCreateAsync(
        string userId,
        string loginAt,
        string? text = null,
        IReadOnlyList<SupportUploadMedia>? media = null,
        string? log = null,
        SupportUploadMedia? logFile = null,
        string? themeId = null,
        bool forceNew = false,
        CancellationToken ct = default)
    {
        media ??= [];
        var logMedia = logFile is null ? Array.Empty<SupportUploadMedia>() : new[] { logFile };
        try
        {
            return await PostTicketWithRetryAsync(
                userId, loginAt, text, media, log, logMedia, themeId, null, forceNew, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsLogRelayFailure(ex) && logMedia.Length > 0)
        {
            AppLog.Append($"support create: log media failed ({ex.Message}), retry text log only");
            return await PostTicketWithRetryAsync(
                userId, loginAt, text, media, log, [], themeId, null, forceNew, ct).ConfigureAwait(false);
        }
    }

    private static async Task<SupportTicket> PostTicketWithRetryAsync(
        string userId, string loginAt, string? text, IReadOnlyList<SupportUploadMedia> media,
        string? log, IReadOnlyList<SupportUploadMedia> logMedia, string? themeId, string? ticketId,
        bool forceNew, CancellationToken ct)
    {
        var delayMs = 400;
        Exception last = new AuthException("ticket post failed", 500);
        for (var attempt = 0; attempt < 3; attempt++)
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
                if (attempt == 2 || !IsRetryableRelay(ex)) throw;
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

    private static bool IsLogRelayFailure(Exception ex)
    {
        var msg = ex.Message ?? "";
        return msg.Contains("лог", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("log", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("fetch failed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Photos go in their own request — CS API forbids mixing text and media.</summary>
    public static async Task<SupportTicket> PostMediaSeparatedAsync(
        string userId, string loginAt, IReadOnlyList<SupportUploadMedia> media, string ticketId,
        CancellationToken ct = default)
    {
        var delayMs = 400;
        Exception last = new AuthException("media upload failed", 500);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await PostTicketAsync(userId, loginAt, media: media, ticketId: ticketId, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                var msg = ex.Message ?? "";
                AppLog.Append($"support media post try{attempt + 1}: {msg}");
                if (attempt == 2 || !IsRetryableRelay(ex)) throw;
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                delayMs *= 2;
            }
        }
        throw last;
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
        var jpeg = await CompressImageToJpegAsync(bytes).ConfigureAwait(false)
                   ?? throw new AuthException("unsupported image", 400);
        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(stem)) stem = "photo";
        return new SupportUploadMedia(Convert.ToBase64String(jpeg), stem + ".jpg", "image/jpeg");
    }

    public static async Task<(string DeviceText, SupportUploadMedia LogFile)> BuildSilentDiagnosticsAsync()
    {
        var device = await DeviceIdentity.LoadAsync().ConfigureAwait(false);
        var ver = AssemblyVersion();
        var sb = new StringBuilder();
        sb.AppendLine($"Demar VPN {Strings.VersionName} ({ver})");
        sb.AppendLine($"UA: {DemarHttp.UserAgent}");
        sb.AppendLine("--- device ---");
        foreach (var (k, v) in device) sb.AppendLine($"{k}={v}");
        var deviceText = sb.ToString().Trim();
        if (deviceText.Length > 8000) deviceText = deviceText[..8000];

        var logBody = AppLog.ReadPersistedUtf8();
        if (string.IsNullOrWhiteSpace(logBody)) logBody = "(empty)";
        // Keep journal small enough for CS Telegram relay (text+photos create path).
        if (logBody.Length > 120_000) logBody = logBody[^120_000..];
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss");
        var upload = new SupportUploadMedia(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(logBody)),
            $"demar_vpn_journal_{stamp}.txt",
            "text/plain");
        return (deviceText, upload);
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

    private static async Task<byte[]?> CompressImageToJpegAsync(byte[] raw)
    {
        try
        {
            using var input = new InMemoryRandomAccessStream();
            await input.WriteAsync(raw.AsBuffer());
            input.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(input);
            var w = (int)decoder.PixelWidth;
            var h = (int)decoder.PixelHeight;
            if (w <= 0 || h <= 0) return null;

            var edge = Math.Max(w, h);
            double scale = edge > MaxPhotoEdge ? (double)MaxPhotoEdge / edge : 1.0;
            var nw = Math.Max(1, (uint)Math.Round(w * scale));
            var nh = Math.Max(1, (uint)Math.Round(h * scale));

            var transform = new BitmapTransform
            {
                ScaledWidth = nw,
                ScaledHeight = nh,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var soft = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

            var quality = 0.50f;
            byte[]? best = null;
            var edgeCap = (uint)Math.Max(nw, nh);
            while (true)
            {
                using var output = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
                var props = new BitmapPropertySet
                {
                    { "ImageQuality", new BitmapTypedValue(quality, Windows.Foundation.PropertyType.Single) },
                };
                await encoder.BitmapProperties.SetPropertiesAsync(props);
                encoder.SetSoftwareBitmap(soft);
                await encoder.FlushAsync();
                output.Seek(0);
                var buf = new byte[output.Size];
                await output.ReadAsync(buf.AsBuffer(), (uint)buf.Length, InputStreamOptions.None);
                best = buf;
                if (buf.Length <= MaxPhotoBytes) break;
                if (quality > 0.18f)
                {
                    quality -= 0.08f;
                    continue;
                }
                // Still too big — shrink pixels further (CS relay prefers small base64).
                if (edgeCap <= 400) break;
                edgeCap = (uint)(edgeCap * 0.75);
                var scale2 = (double)edgeCap / Math.Max(w, h);
                var nw2 = Math.Max(1u, (uint)Math.Round(w * scale2));
                var nh2 = Math.Max(1u, (uint)Math.Round(h * scale2));
                soft.Dispose();
                var transform2 = new BitmapTransform
                {
                    ScaledWidth = nw2,
                    ScaledHeight = nh2,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };
                input.Seek(0);
                soft = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform2,
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                quality = 0.45f;
            }
            soft.Dispose();
            return best is { Length: > 0 } ? best : null;
        }
        catch (Exception ex)
        {
            AppLog.Append($"support jpeg: {ex.Message}");
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
