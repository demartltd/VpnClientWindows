namespace DemarVpn.Domain.Vpn;

/// <summary>Remote sing-box rule-set from API <c>clientConfigJson.route.rule_set</c>.</summary>
public sealed record ApiBypassRuleSet(string Tag, string Url)
{
    public bool IsGeosite => Tag.StartsWith("geosite", StringComparison.Ordinal);
}
