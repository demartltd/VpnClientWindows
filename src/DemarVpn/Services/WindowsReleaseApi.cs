using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services;

public sealed record WindowsRelease(string Version, string Asset, string Sha256, string DownloadUrl);

/// <summary>Windows release metadata from vpn.example.invalid/windows/version (parity AndroidReleaseApi).</summary>
public static class WindowsReleaseApi
{
    public static string DownloadUrl => $"{DemarHttp.VpnApiBaseUrl}/windows/download";
    public static string BetaUrl => $"{DemarHttp.VpnApiBaseUrl}/windows/beta";

    public static async Task<WindowsRelease?> FetchAsync(bool preferBeta = false, CancellationToken ct = default)
    {
        try
        {
            var json = await DemarHttp.GetJsonAsync($"{DemarHttp.VpnApiBaseUrl}/windows/version", ct: ct);
            return Parse(json, preferBeta);
        }
        catch (Exception ex)
        {
            AppLog.Append($"update: version check failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>Visible for tests.</summary>
    public static WindowsRelease? Parse(JsonObject json, bool preferBeta)
    {
        var stable = ReleaseFrom(json, DownloadUrl);
        if (stable is null) return null;
        if (!preferBeta) return stable;
        if (!json.TryGetPropertyValue("beta", out var betaNode) || betaNode is not JsonObject betaObj)
            return stable;
        var beta = ReleaseFrom(betaObj, BetaUrl);
        if (beta is null) return stable;
        return IsNewer(beta.Version, stable.Version) ? beta : stable;
    }

    public static bool IsNewer(string latest, string local)
    {
        if (!TryParseVersion(latest, out var remote)) return false;
        if (!TryParseVersion(local, out var current)) return true;
        return remote > current;
    }

    public static bool TryParseVersion(string raw, out Version version)
    {
        version = new Version(0, 0);
        var s = raw.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out var major))
        {
            version = new Version(major, 0);
            return true;
        }
        return Version.TryParse(s, out version!);
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    static WindowsRelease? ReleaseFrom(JsonObject json, string downloadUrl)
    {
        var version = DemarHttp.OptString(json, "version");
        var sha256 = DemarHttp.OptString(json, "sha256")?.ToLowerInvariant();
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(sha256)) return null;
        var asset = DemarHttp.OptString(json, "asset");
        if (string.IsNullOrEmpty(asset)) asset = "Updater.exe";
        return new WindowsRelease(version, asset, sha256, downloadUrl);
    }
}
