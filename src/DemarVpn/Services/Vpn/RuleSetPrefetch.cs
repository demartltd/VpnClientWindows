using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

/// <summary>Prefetch .srs for sing-box <c>initial_path</c> (parity Android).</summary>
public static class RuleSetPrefetch
{
    const int MaxBytes = 8 * 1024 * 1024;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<string?> TryEnsureAdsAsync(CancellationToken ct = default)
    {
        try
        {
            var path = await EnsureAsync(SingBoxTags.AdsRuleSetTag, SingBoxTags.AdsRuleSetUrl, ct).ConfigureAwait(false);
            AppLog.Append($"[vpn] ads prefetch ok {path}");
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] ads prefetch: {ex.Message}");
            return null;
        }
    }

    static async Task<string> EnsureAsync(string tag, string url, CancellationToken ct)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "demar", "rule-sets");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{tag}.srs");
        if (File.Exists(file) && new FileInfo(file).Length > 0) return file;

        var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        if (bytes.Length is 0 or > MaxBytes)
            throw new InvalidOperationException($"rule-set {tag} invalid size");
        await File.WriteAllBytesAsync(file, bytes, ct).ConfigureAwait(false);
        return file;
    }
}
