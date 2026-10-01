using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

/// <summary>Bundled ads rule-set path for sing-box <c>type: local</c>.</summary>
public static class RuleSetPrefetch
{
    public static string? TryEnsureAds()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Assets", "rule-sets", $"{SingBoxTags.AdsRuleSetTag}.srs");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                AppLog.Append("[vpn] ads asset missing");
                return null;
            }
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] ads ensure: {ex.Message}");
            return null;
        }
    }
}
