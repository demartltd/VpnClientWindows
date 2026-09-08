namespace DemarVpn.Domain.Vpn;

public sealed record SingBoxTunOptions(string DnsFinal, IReadOnlyList<string> BootstrapDomains);

public sealed record SingBoxConnectPlan
{
    public required string UplinkHost { get; init; }
    public required Dictionary<string, object?> ProxyOutbound { get; init; }
    public required IReadOnlyList<string> DirectIPs { get; init; }
    public required SingBoxTunOptions Tun { get; init; }
    public IReadOnlyList<ApiBypassRuleSet> BypassRuleSets { get; init; } = [];

    public SingBoxConnectPlan WithExtraDirectIps(IEnumerable<string> ips)
    {
        var merged = DirectIPs.Concat(ips).Distinct().ToList();
        if (merged.Count == DirectIPs.Count) return this;
        return this with { DirectIPs = merged };
    }

    public static SingBoxConnectPlan Hysteria2(
        Hysteria2Profile profile,
        IReadOnlyList<ApiBypassRuleSet>? bypassRuleSets = null)
    {
        var tls = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["server_name"] = profile.TlsServerName,
            ["alpn"] = profile.ResolvedAlpn.ToList(),
        };
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "hysteria2",
            ["tag"] = SingBoxTags.Proxy,
            ["server"] = profile.Server,
            ["server_port"] = profile.Port,
            ["password"] = profile.Password,
            ["udp_fragment"] = true,
            ["tls"] = tls,
            ["domain_resolver"] = new Dictionary<string, object?>
            {
                ["server"] = SingBoxTags.DnsDirect,
                ["strategy"] = "ipv4_only",
            },
        };
        if (!string.IsNullOrEmpty(profile.ObfsPassword))
        {
            outbound["obfs"] = new Dictionary<string, object?>
            {
                ["type"] = "salamander",
                ["password"] = profile.ObfsPassword,
            };
        }
        if (profile.UpMbps is > 0) outbound["up_mbps"] = profile.UpMbps;
        if (profile.DownMbps is > 0) outbound["down_mbps"] = profile.DownMbps;

        return new SingBoxConnectPlan
        {
            UplinkHost = profile.Server,
            ProxyOutbound = outbound,
            DirectIPs = DirectIps(profile.Server),
            Tun = new SingBoxTunOptions(SingBoxTags.DnsRemote, BootstrapDomains(profile.Server, profile.TlsServerName)),
            BypassRuleSets = bypassRuleSets ?? [],
        };
    }

    private static List<string> BootstrapDomains(string server, string tlsServerName)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tlsServerName };
        if (HostUtils.IsHostname(server))
            domains.Add(server.Trim());
        return domains.ToList();
    }

    private static List<string> DirectIps(string host)
    {
        var trimmed = host.Trim();
        return HostUtils.IsLiteralIpv4(trimmed) ? [trimmed] : [];
    }
}
