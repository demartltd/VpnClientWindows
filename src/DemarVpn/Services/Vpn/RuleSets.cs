using DemarVpn.Domain.Vpn;
using DemarVpn.Domain.Vpn.Tun;

namespace DemarVpn.Services.Vpn;

/// <summary>
/// Bundled local <c>.srs</c> (no runtime HTTP) + DNS cache stamp for Ad Block / GEO / bypass.
/// </summary>
public static class RuleSets
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "demar");

    private static readonly string CacheDb = Path.Combine(CacheDir, "cache.db");
    private static readonly string CacheStamp = Path.Combine(CacheDir, "cache.stamp");

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

    /// <summary>Keep <c>cache.db</c> when stamp matches; else wipe (store_dns retains rejects).</summary>
    public static void AlignDnsCache(bool ads, IEnumerable<ApiBypassRuleSet> ruleSets, TunBypassOptions bypass)
    {
        var stamp = DnsStamp(ads, ruleSets, bypass);
        try
        {
            Directory.CreateDirectory(CacheDir);
            var prev = File.Exists(CacheStamp) ? File.ReadAllText(CacheStamp).Trim() : null;
            if (prev == stamp && File.Exists(CacheDb)) return;

            TryDelete(CacheDb);
            foreach (var side in Directory.EnumerateFiles(CacheDir, "cache.db*"))
                TryDelete(side);

            File.WriteAllText(CacheStamp, stamp);
            if (prev is not null && prev != stamp)
                AppLog.Append("[vpn] dns cache reset (adblock/geo/bypass changed)");
        }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] dns cache align: {ex.Message}");
        }
    }

    public static string DnsStamp(
        bool ads,
        IEnumerable<ApiBypassRuleSet> ruleSets,
        TunBypassOptions bypass)
    {
        var tags = ruleSets.Select(rs => rs.Tag);
        if (ads) tags = tags.Append(SingBoxTags.AdsRuleSetTag);
        var rs = string.Join(',', tags.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal));
        return $"ads={ads};rs={rs};h={string.Join(',', bypass.BypassHosts)};p={string.Join(',', bypass.BypassExes)}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignore */ }
    }
}
