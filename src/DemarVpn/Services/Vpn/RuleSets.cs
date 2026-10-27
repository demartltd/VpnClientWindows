using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

/// <summary>Bundled local <c>.srs</c> (no runtime HTTP).</summary>
public static class RuleSets
{
    public static string Dir { get; } =
        Path.Combine(AppContext.BaseDirectory, "Assets", "rule-sets");

    public static string PathFor(string tag) => Path.Combine(Dir, $"{tag}.srs");

    public static bool Exists(string tag)
    {
        try
        {
            var path = PathFor(tag);
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Drop missing GEO tags; resolve ads asset. Logs gaps.</summary>
    public static (SingBoxConnectPlan Plan, bool Ads) Prepare(SingBoxConnectPlan plan, bool wantAds)
    {
        var ready = new List<ApiBypassRuleSet>();
        var missing = new List<string>();
        foreach (var rs in plan.BypassRuleSets)
        {
            if (Exists(rs.Tag)) ready.Add(rs);
            else missing.Add(rs.Tag);
        }
        if (missing.Count > 0)
            AppLog.Append($"[vpn] rule-set missing: {string.Join(",", missing)}");

        var ads = wantAds && Exists(SingBoxTags.AdsRuleSetTag);
        if (wantAds && !ads)
            AppLog.Append("[vpn] ads asset missing");

        var next = missing.Count == 0 ? plan : plan.WithBypassRuleSets(ready);
        return (next, ads);
    }
}
