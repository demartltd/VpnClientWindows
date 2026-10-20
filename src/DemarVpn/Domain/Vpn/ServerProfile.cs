using System.Globalization;

namespace DemarVpn.Domain.Vpn;

public enum ServerTier
{
    Free,
    Dev,
    Tester,
    Plus,
    Vip,
}

public static class ServerTierLabels
{
    public static string Label(this ServerTier tier) => tier switch
    {
        ServerTier.Dev => "DEV",
        ServerTier.Tester => "TESTER",
        ServerTier.Plus => "PLUS",
        ServerTier.Vip => "VIP",
        _ => "FREE",
    };

    public static int PaywallRank(this ServerTier tier) => tier switch
    {
        ServerTier.Tester => 1,
        ServerTier.Plus => 2,
        ServerTier.Vip or ServerTier.Dev => 3,
        _ => 0,
    };

    public static int SubscriptionRank(string? raw) => (raw ?? "free").Trim().ToLowerInvariant() switch
    {
        "tester" => 1,
        "plus" => 2,
        "vip" or "dev" => 3,
        _ => 0,
    };

    public static bool RequiresUpgrade(this ServerTier tier, string? subscription) =>
        SubscriptionRank(subscription) < tier.PaywallRank();
}

public enum ServerProtocol
{
    Hysteria2,
    Trojan,
    VlessReality,
}

public static class ServerProtocolLabels
{
    public static string ApiKey(this ServerProtocol protocol) => protocol switch
    {
        ServerProtocol.Trojan => "trojan",
        ServerProtocol.VlessReality => "vless_reality",
        _ => "hysteria2",
    };

    public static string Label(this ServerProtocol protocol) => protocol switch
    {
        ServerProtocol.Trojan => "TRJ",
        ServerProtocol.VlessReality => "VLESS",
        _ => "HY2",
    };

    public static string FullLabel(this ServerProtocol protocol) => protocol switch
    {
        ServerProtocol.Trojan => "Trojan",
        ServerProtocol.VlessReality => "VLESS Reality",
        _ => "Hysteria2",
    };

    public static ServerProtocol? FromApi(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim().ToLowerInvariant();
        foreach (var p in Enum.GetValues<ServerProtocol>())
            if (p.ApiKey() == key) return p;
        return null;
    }
}

public sealed record ServerProfile(
    string Id,
    string DisplayName,
    IProxyProfile Profile,
    string? CountryCode = null,
    string? CountryName = null,
    string? City = null,
    IReadOnlyList<ApiBypassRuleSet>? BypassRuleSets = null,
    int? LoadPercent = null)
{
    public SingBoxConnectPlan ConnectPlan =>
        Profile.ToConnectPlan(BypassRuleSets ?? []);
    public int UplinkPort => Profile.Port;
    public ServerProtocol Protocol => Profile.Protocol;
    public int EffectiveLoadPercent => Math.Clamp(LoadPercent ?? 10, 0, 100);

    public ServerTier Tier
    {
        get
        {
            var mid = DisplayName.Split('-').ElementAtOrDefault(1)?.ToUpperInvariant();
            return mid switch
            {
                "DEV" => ServerTier.Dev,
                "TESTER" => ServerTier.Tester,
                "PLUS" => ServerTier.Plus,
                "VIP" => ServerTier.Vip,
                _ => ServerTier.Free,
            };
        }
    }

    public int? TagNumber
    {
        get
        {
            var i = DisplayName.LastIndexOf('-');
            if (i < 0 || i >= DisplayName.Length - 1) return null;
            return int.TryParse(DisplayName.AsSpan(i + 1), out var n) ? n : null;
        }
    }

    public bool IsLocked => !Profile.HasCredentials;

    public string TitleName =>
        string.IsNullOrWhiteSpace(CountryName) ? DisplayName : CountryName;

    public string ListSubtitle
    {
        get
        {
            if (TagNumber is not { } n || string.IsNullOrWhiteSpace(City)) return "";
            return $"{City} #{n}";
        }
    }

    public static ServerProfile FromProxy(
        IProxyProfile profile,
        string? countryCode = null,
        string? countryName = null,
        string? city = null,
        string? id = null,
        IReadOnlyList<ApiBypassRuleSet>? bypassRuleSets = null,
        int? loadPercent = null) => new(
        id ?? $"{profile.Protocol.ApiKey()}:{profile.DisplayName}",
        profile.DisplayName,
        profile,
        countryCode,
        countryName,
        city,
        bypassRuleSets ?? [],
        loadPercent);

    public static ServerProfile FromTag(
        IProxyProfile profile,
        string? city = null,
        string? id = null,
        IReadOnlyList<ApiBypassRuleSet>? bypassRuleSets = null,
        int? loadPercent = null)
    {
        var code = CountryCodeFromTag(profile.DisplayName);
        return FromProxy(profile, code, CountryNameFromCode(code), city, id, bypassRuleSets, loadPercent);
    }

    public static string? CountryCodeFromTag(string tag)
    {
        var dash = tag.IndexOf('-');
        if (dash <= 0) return null;
        var code = tag[..dash];
        return code.Length == 2 && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : null;
    }

    public static string? CountryNameFromCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return null;
        try { return new RegionInfo(countryCode).EnglishName; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Only tiers present in the list (Android/Apple §9).</summary>
    public static IReadOnlyList<ServerTier> VisibleTiers(IEnumerable<ServerProfile> servers)
    {
        var list = servers as IList<ServerProfile> ?? servers.ToList();
        return Enum.GetValues<ServerTier>().Where(t => list.Any(p => p.Tier == t)).ToList();
    }

    /// <summary>Only protocols present in the list (supported enum ∩ API).</summary>
    public static IReadOnlyList<ServerProtocol> VisibleProtocols(IEnumerable<ServerProfile> servers)
    {
        var list = servers as IList<ServerProfile> ?? servers.ToList();
        return Enum.GetValues<ServerProtocol>().Where(p => list.Any(s => s.Protocol == p)).ToList();
    }

    public static bool MatchesFilters(
        IEnumerable<ServerProfile> servers,
        IReadOnlySet<ServerTier> tiers,
        IReadOnlySet<ServerProtocol> protocols)
    {
        var list = servers as IList<ServerProfile> ?? servers.ToList();
        return list.Count == 0 || list.Any(s => tiers.Contains(s.Tier) && protocols.Contains(s.Protocol));
    }

    /// <summary>Unlocked first, then favorites, then A–Z by displayName.</summary>
    public static IReadOnlyList<ServerProfile> FilterServers(
        IEnumerable<ServerProfile> servers,
        IReadOnlySet<ServerTier> tiers,
        IReadOnlySet<ServerProtocol> protocols,
        IReadOnlySet<string>? favoriteIds = null) =>
        servers
            .Where(s => tiers.Contains(s.Tier) && protocols.Contains(s.Protocol))
            .OrderBy(s => s.IsLocked)
            .ThenByDescending(s => favoriteIds is not null && favoriteIds.Contains(s.Id))
            .ThenBy(s => s.DisplayName, StringComparer.Ordinal)
            .ToList();
}
