namespace DemarVpn.Domain.Vpn;

/// <summary>
/// Bypass rule-set tag from API <c>clientConfigJson.route.rule_set</c>.
/// Geosite/geoip → bundled local <c>.srs</c>; API <c>url</c> ignored at runtime.
/// </summary>
public sealed record ApiBypassRuleSet(string Tag)
{
    public bool IsGeosite => Tag.StartsWith("geosite", StringComparison.Ordinal);

    /// <summary>ISO country from <c>geoip-xx</c> tags (direct / outside VPN).</summary>
    public string? GeoipCountryCode
    {
        get
        {
            var t = Tag.Trim().ToLowerInvariant();
            if (!t.StartsWith("geoip-", StringComparison.Ordinal)) return null;
            var code = t["geoip-".Length..].ToUpperInvariant();
            return code.Length == 2 && code.All(char.IsAsciiLetter) ? code : null;
        }
    }

    public static IReadOnlyList<string> DirectCountryCodes(IEnumerable<ApiBypassRuleSet>? sets) =>
        (sets ?? [])
            .Select(s => s.GeoipCountryCode)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
}
