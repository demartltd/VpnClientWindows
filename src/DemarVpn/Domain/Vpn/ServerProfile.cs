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
}

public static class ServerProtocolLabels
{
    public static string Label(this ServerProtocol protocol) => protocol switch
    {
        _ => "Hysteria2",
    };
}

public sealed record ServerProfile(
    string Id,
    string DisplayName,
    Hysteria2Profile Profile,
    string? CountryCode = null,
    string? CountryName = null,
    string? City = null,
    IReadOnlyList<ApiBypassRuleSet>? BypassRuleSets = null)
{
    public SingBoxConnectPlan ConnectPlan =>
        SingBoxConnectPlan.Hysteria2(Profile, BypassRuleSets ?? []);
    public int UplinkPort => Profile.Port;
    public ServerProtocol Protocol => ServerProtocol.Hysteria2;

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

    public string ListTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CountryName)) return DisplayName;
            return $"{CountryName} | {Tier.Label()}";
        }
    }

    public string ListSubtitle
    {
        get
        {
            if (TagNumber is not { } n || string.IsNullOrWhiteSpace(City)) return "";
            return $"{City} #{n}";
        }
    }

    public static ServerProfile Hysteria2(
        Hysteria2Profile profile,
        string? countryCode = null,
        string? countryName = null,
        string? city = null,
        string? id = null,
        IReadOnlyList<ApiBypassRuleSet>? bypassRuleSets = null) => new(
        id ?? $"hysteria2:{profile.DisplayName}",
        profile.DisplayName,
        profile,
        countryCode,
        countryName,
        city,
        bypassRuleSets ?? []);

    public static ServerProfile Hysteria2FromTag(
        Hysteria2Profile profile,
        string? city = null,
        string? id = null,
        IReadOnlyList<ApiBypassRuleSet>? bypassRuleSets = null)
    {
        var code = CountryCodeFromTag(profile.DisplayName);
        return Hysteria2(profile, code, CountryNameFromCode(code), city, id, bypassRuleSets);
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

    public static IReadOnlyList<ServerTier> VisibleTiers(IEnumerable<ServerProfile> servers)
    {
        var list = servers as IList<ServerProfile> ?? servers.ToList();
        return Enum.GetValues<ServerTier>()
            .Where(t => (t != ServerTier.Dev && t != ServerTier.Tester) || list.Any(p => p.Tier == t))
            .ToList();
    }

    public static IReadOnlyList<ServerProfile> Filter(
        IEnumerable<ServerProfile> servers, ServerProtocol protocol, ServerTier? tier) =>
        servers
            .Where(s => s.Protocol == protocol && (tier is null || s.Tier == tier))
            .OrderBy(s => s.DisplayName)
            .ToList();
}
