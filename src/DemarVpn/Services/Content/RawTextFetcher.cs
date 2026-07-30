using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace DemarVpn.Services.Content;

/// <summary>HTTPS raw text fetch — parity with Android RawTextFetcher.</summary>
public static class RawTextFetcher
{
    private const int MaxBytes = 512 * 1024;
    private const int ConnectTimeoutMs = 10_000;
    private const int ReadTimeoutMs = 15_000;

    private static readonly HttpClient Http = CreateClient();
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<string> GetAsync(string url, CancellationToken ct = default)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only HTTPS URLs are allowed");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("text/plain, text/markdown, */*");
        req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return Decode((int)resp.StatusCode, bytes);
    }

    /// <summary>Pure decode for unit tests — no network.</summary>
    public static string Decode(int statusCode, byte[] bytes)
    {
        if (statusCode is < 200 or > 299)
            throw new IOException($"HTTP {statusCode}");
        if (bytes.Length > MaxBytes)
            throw new IOException("Response too large");
        try
        {
            var text = Utf8Strict.GetString(bytes).Trim();
            if (text.Length == 0) throw new IOException("Document empty or invalid");
            return text;
        }
        catch (DecoderFallbackException)
        {
            throw new IOException("Document empty or invalid");
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs),
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs + ReadTimeoutMs),
        };
    }
}
