namespace DemarVpn.Domain.Vpn;

public sealed record SingBoxTunOptions(string DnsFinal, IReadOnlyList<string> BootstrapDomains, bool StrictRoute);

public sealed record SingBoxConnectPlan
{
    public required string UplinkHost { get; init; }
    public required Dictionary<string, object?> ProxyOutbound { get; init; }
    public required IReadOnlyList<string> DirectIPs { get; init; }
    public required SingBoxTunOptions Tun { get; init; }

    public SingBoxConnectPlan WithExtraDirectIps(IEnumerable<string> ips)
    {
        var merged = DirectIPs.Concat(ips).Distinct().ToList();
        if (merged.Count == DirectIPs.Count) return this;
        return this with { DirectIPs = merged };
    }

    public static SingBoxConnectPlan Naive(NaiveProfile profile)
    {
        var bootstrap = BootstrapDomains(profile);
        return new SingBoxConnectPlan
        {
            UplinkHost = profile.Server,
            ProxyOutbound = new Dictionary<string, object?>
            {
                ["type"] = "naive",
                ["tag"] = SingBoxTags.Proxy,
                ["server"] = profile.Server,
                ["server_port"] = profile.Port,
                ["username"] = profile.Username,
                ["password"] = profile.Password,
                ["connect_timeout"] = "10s",
                ["domain_resolver"] = new Dictionary<string, object?>
                {
                    ["server"] = SingBoxTags.DnsDirect,
                    ["strategy"] = "ipv4_only",
                },
                ["udp_over_tcp"] = new Dictionary<string, object?> { ["enabled"] = true, ["version"] = 2 },
                ["quic"] = false,
                ["tls"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["server_name"] = profile.TlsServerName,
                },
            },
            DirectIPs = DirectIps(profile.Server),
            Tun = new SingBoxTunOptions(SingBoxTags.DnsRemote, bootstrap, true),
        };
    }

    private static List<string> BootstrapDomains(NaiveProfile profile)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { profile.TlsServerName };
        if (HostUtils.IsHostname(profile.Server))
            domains.Add(profile.Server.Trim());
        return domains.ToList();
    }

    private static List<string> DirectIps(string host)
    {
        var trimmed = host.Trim();
        return HostUtils.IsLiteralIpv4(trimmed) ? [trimmed] : [];
    }
}
