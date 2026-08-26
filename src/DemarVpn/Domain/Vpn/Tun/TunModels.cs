namespace DemarVpn.Domain.Vpn.Tun;

public sealed record TunAppRef(string Id, string Name);

public sealed record TunSiteRef(string Host, string Name);

public sealed record TunAppGroup(
    string Id,
    string Name,
    IReadOnlyList<TunAppRef> Apps,
    bool EnabledByDefault,
    bool IsWhiteList)
{
    public static string IdFor(string name) => name.Trim().ToLowerInvariant();
}

public sealed record TunSiteGroup(
    string Id,
    string Name,
    IReadOnlyList<TunSiteRef> Sites,
    bool EnabledByDefault,
    bool IsWhiteList)
{
    public static string IdFor(string name) => name.Trim().ToLowerInvariant();
}

public sealed record TunCatalog(
    IReadOnlyList<TunAppGroup> AppGroups,
    IReadOnlyList<TunSiteGroup> SiteGroups)
{
    public static TunCatalog Empty { get; } = new([], []);
}

public sealed record TunSettings(
    IReadOnlyList<string> BypassExes,
    IReadOnlyDictionary<string, bool> AppGroupOverrides,
    IReadOnlyDictionary<string, bool> SiteGroupOverrides,
    bool RulesEnabled = true)
{
    private static readonly Dictionary<string, bool> EmptyOverrides = new(StringComparer.OrdinalIgnoreCase);

    public static TunSettings Empty { get; } = new([], EmptyOverrides, EmptyOverrides);
}

public sealed record TunBypassOptions(IReadOnlyList<string> BypassExes, IReadOnlyList<string> BypassHosts)
{
    public static TunBypassOptions Empty { get; } = new([], []);
}

public static class TunExeNames
{
    public const string SingBox = "sing-box.exe";
    public const string AppExe = "Demar VPN.exe";

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return Path.GetFileName(raw.Trim()).ToLowerInvariant();
    }

    public static bool IsExeName(string? raw)
    {
        var id = Normalize(raw);
        return id.Length > 4 && id.EndsWith(".exe", StringComparison.Ordinal);
    }

    public static bool IsSelf(string? exe) =>
        Normalize(exe) is var n && (n.Length == 0 || n is SingBox or "demar vpn.exe");
}

public static class TunEffective
{
    public static bool IsRulesEnabled(this TunSettings settings, bool isFree) =>
        isFree || settings.RulesEnabled;

    public static bool IsAppGroupEnabled(this TunSettings settings, TunAppGroup group, bool isFree) =>
        ResolveEnabled(isFree, settings.AppGroupOverrides, group.Id, group.EnabledByDefault);

    public static bool IsSiteGroupEnabled(this TunSettings settings, TunSiteGroup group, bool isFree) =>
        ResolveEnabled(isFree, settings.SiteGroupOverrides, group.Id, group.EnabledByDefault);

    public static IReadOnlyList<string> EffectiveBypassExes(
        this TunSettings settings, TunCatalog catalog, bool isFree)
    {
        if (!settings.IsRulesEnabled(isFree)) return [];
        var fromGroups = catalog.AppGroups
            .Where(g => settings.IsAppGroupEnabled(g, isFree))
            .SelectMany(g => g.Apps.Select(a => TunExeNames.Normalize(a.Id)))
            .Where(id => id.Length > 0 && !TunExeNames.IsSelf(id));
        return settings.BypassExes
            .Select(TunExeNames.Normalize)
            .Where(id => id.Length > 0 && !TunExeNames.IsSelf(id))
            .Concat(fromGroups)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> EffectiveBypassHosts(
        this TunSettings settings, TunCatalog catalog, bool isFree)
    {
        if (!settings.IsRulesEnabled(isFree)) return [];
        return catalog.SiteGroups
            .Where(g => settings.IsSiteGroupEnabled(g, isFree))
            .SelectMany(g => g.Sites.Select(s => s.Host.Trim()))
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static TunBypassOptions EffectiveBypass(
        this TunSettings settings, TunCatalog catalog, bool isFree) =>
        new(settings.EffectiveBypassExes(catalog, isFree), settings.EffectiveBypassHosts(catalog, isFree));

    public static string Fingerprint(this TunSettings settings, TunCatalog catalog, bool isFree)
    {
        var bypass = settings.EffectiveBypass(catalog, isFree);
        var appGroups = catalog.AppGroups
            .Where(g => settings.IsAppGroupEnabled(g, isFree))
            .Select(g => g.Id)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var siteGroups = catalog.SiteGroups
            .Where(g => settings.IsSiteGroupEnabled(g, isFree))
            .Select(g => g.Id)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var on = settings.IsRulesEnabled(isFree);
        return $"p={string.Join(',', bypass.BypassExes)};h={string.Join(',', bypass.BypassHosts)};" +
               $"ag={string.Join(',', appGroups)};sg={string.Join(',', siteGroups)};r={on}";
    }

    private static bool ResolveEnabled(
        bool isFree,
        IReadOnlyDictionary<string, bool>? overrides,
        string groupId,
        bool enabledByDefault)
    {
        if (isFree) return true;
        if (overrides is not null && overrides.TryGetValue(groupId, out var user)) return user;
        return enabledByDefault;
    }
}
