using System.Text.RegularExpressions;

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
    IReadOnlyList<string> BypassHosts,
    IReadOnlyDictionary<string, bool> AppGroupOverrides,
    IReadOnlyDictionary<string, bool> SiteGroupOverrides,
    bool RulesEnabled = true,
    bool? AdBlockEnabled = null)
{
    private static readonly Dictionary<string, bool> EmptyOverrides = new(StringComparer.OrdinalIgnoreCase);

    public static TunSettings Empty { get; } = new([], [], EmptyOverrides, EmptyOverrides);
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

/// <summary>URL/host → apex host for site bypass (parity Android parseBypassHost).</summary>
public static class TunBypassHosts
{
    // Label.Label… with ≥1 dot; rejects bare words.
    private static readonly Regex DomainRe = new(
        @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Ipv4Re = new(
        @"^\d{1,3}(\.\d{1,3}){3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Trim('.');
        return s.Length == 0 ? null : s;
    }

    public static string? Parse(string? raw)
    {
        var t = raw?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        var withScheme = t.Contains("://", StringComparison.Ordinal) || t.StartsWith("//", StringComparison.Ordinal)
            ? t
            : "https://" + t;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host?.ToLowerInvariant();
        if (string.IsNullOrEmpty(host)) return null;
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        var cleaned = Clean(host);
        return cleaned is not null && IsBypassDomain(cleaned) ? cleaned : null;
    }

    public static IReadOnlyList<string> ParseMany(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Parse)
            .Where(h => h is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsBypassDomain(string host) =>
        DomainRe.IsMatch(host) && !Ipv4Re.IsMatch(host);
}

public static class TunEffective
{
    public static bool IsRulesEnabled(this TunSettings settings, bool isFree) =>
        isFree || settings.RulesEnabled;

    public static bool IsAdBlockEnabled(this TunSettings settings, bool isFree) =>
        !isFree && (settings.AdBlockEnabled ?? true);

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
        // Free: custom BypassExes ignored (Plus feature); prefs kept for Renew.
        var custom = isFree
            ? Enumerable.Empty<string>()
            : settings.BypassExes
                .Select(TunExeNames.Normalize)
                .Where(id => id.Length > 0 && !TunExeNames.IsSelf(id));
        return custom
            .Concat(fromGroups)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> EffectiveBypassHosts(
        this TunSettings settings, TunCatalog catalog, bool isFree)
    {
        if (!settings.IsRulesEnabled(isFree)) return [];
        var fromGroups = catalog.SiteGroups
            .Where(g => settings.IsSiteGroupEnabled(g, isFree))
            .SelectMany(g => g.Sites.Select(s => TunBypassHosts.Clean(s.Host)))
            .Where(h => h is not null)
            .Cast<string>();
        // Free: custom BypassHosts ignored (Plus feature); prefs kept for Renew.
        var custom = isFree
            ? Enumerable.Empty<string>()
            : settings.BypassHosts
                .Select(TunBypassHosts.Parse)
                .Where(h => h is not null)
                .Cast<string>();
        return custom
            .Concat(fromGroups)
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
        var ads = settings.IsAdBlockEnabled(isFree);
        return $"p={string.Join(',', bypass.BypassExes)};h={string.Join(',', bypass.BypassHosts)};" +
               $"ag={string.Join(',', appGroups)};sg={string.Join(',', siteGroups)};r={on};ads={ads}";
    }

    private static bool ResolveEnabled(
        bool isFree,
        IReadOnlyDictionary<string, bool>? overrides,
        string groupId,
        bool enabledByDefault)
    {
        if (isFree) return enabledByDefault;
        if (overrides is not null && overrides.TryGetValue(groupId, out var user)) return user;
        return enabledByDefault;
    }
}

public static class TunSubscription
{
    /// <summary>Account wins over session so stale session "free" cannot mask paid.</summary>
    public static bool IsFree(string? accountSub, string? sessionSub)
    {
        var sub = accountSub ?? sessionSub;
        return string.IsNullOrWhiteSpace(sub)
            || sub.Equals("free", StringComparison.OrdinalIgnoreCase);
    }
}
