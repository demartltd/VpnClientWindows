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

    public SingBoxConnectPlan WithResolvedUplink(string ip)
    {
        var trimmed = ip.Trim();
        if (trimmed.Length == 0) return this;
        var outbound = new Dictionary<string, object?>(ProxyOutbound) { ["server"] = trimmed };
        return this with { ProxyOutbound = outbound };
    }

    public SingBoxConnectPlan WithBypassRuleSets(IReadOnlyList<ApiBypassRuleSet> sets) =>
        this with { BypassRuleSets = sets.DistinctBy(rs => rs.Tag).ToList() };

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
            ["domain_resolver"] = DomainResolver(),
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

        return Of(
            profile.Server,
            outbound,
            BootstrapDomains(profile.Server, profile.TlsServerName),
            bypassRuleSets);
    }

    public static SingBoxConnectPlan Trojan(
        TrojanProfile profile,
        IReadOnlyList<ApiBypassRuleSet>? bypassRuleSets = null)
    {
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "trojan",
            ["tag"] = SingBoxTags.Proxy,
            ["server"] = profile.Server,
            ["server_port"] = profile.Port,
            ["password"] = profile.Password,
            ["tls"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["server_name"] = profile.TlsServerName,
            },
            ["multiplex"] = new Dictionary<string, object?> { ["enabled"] = true },
            ["domain_resolver"] = DomainResolver(),
        };
        return Of(
            profile.Server,
            outbound,
            BootstrapDomains(profile.Server, profile.TlsServerName),
            bypassRuleSets);
    }

    public static SingBoxConnectPlan VlessReality(
        VlessRealityProfile profile,
        IReadOnlyList<ApiBypassRuleSet>? bypassRuleSets = null)
    {
        // Route/DNS bootstrap = uplink host only (Reality SNI is not a dial target).
        var bootstrap = HostUtils.IsHostname(profile.Server)
            ? new List<string> { profile.Server.Trim() }
            : [];
        var tls = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["server_name"] = profile.TlsServerName,
            ["utls"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["fingerprint"] = "chrome",
            },
            ["reality"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["public_key"] = profile.PublicKey,
                ["short_id"] = profile.ShortId,
            },
        };
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "vless",
            ["tag"] = SingBoxTags.Proxy,
            ["server"] = profile.Server,
            ["server_port"] = profile.Port,
            ["uuid"] = profile.Uuid,
            ["tls"] = tls,
            ["domain_resolver"] = DomainResolver(),
        };
        if (!string.IsNullOrWhiteSpace(profile.Flow))
            outbound["flow"] = profile.Flow;
        // Omit network: both tcp+udp; Vision still uses TCP command.
        // Vision flow is incompatible with multiplex — omit even if API sent it.
        return Of(profile.Server, outbound, bootstrap, bypassRuleSets);
    }

    private static SingBoxConnectPlan Of(
        string uplink,
        Dictionary<string, object?> outbound,
        IReadOnlyList<string> bootstrap,
        IReadOnlyList<ApiBypassRuleSet>? bypass) => new()
    {
        UplinkHost = uplink,
        ProxyOutbound = outbound,
        DirectIPs = DirectIps(uplink),
        Tun = new SingBoxTunOptions(SingBoxTags.DnsRemote, bootstrap),
        BypassRuleSets = bypass ?? [],
    };

    private static Dictionary<string, object?> DomainResolver() => new()
    {
        ["server"] = SingBoxTags.DnsDirect,
        ["strategy"] = "ipv4_only",
    };

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
