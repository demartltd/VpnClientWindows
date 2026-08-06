using DemarVpn.Services.Auth;

namespace DemarVpn.Services;

/// <summary>Latest Windows release from vpn.example.invalid/windows/version.</summary>
public static class WindowsReleaseApi
{
    public static async Task<string?> FetchLatestVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await DemarHttp.GetJsonAsync($"{DemarHttp.VpnApiBaseUrl}/windows/version", ct: ct);
            return DemarHttp.OptString(json, "version");
        }
        catch (Exception ex)
        {
            AppLog.Append($"windows version: {ex.Message}");
            return null;
        }
    }
}
