using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Domain.Vpn.Tun;

namespace DemarVpn.Services.Vpn;

public static class SingBoxConfigBuilder
{
    private static readonly string[] TunExcludeCidrs =
        ["127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16"];

    public static string BuildTunConfigJson(
        SingBoxConnectPlan plan,
        TunBypassOptions? bypass = null,
        string tunName = "DemarVPN",
        int mtu = 1492,
        string logLevel = "warning") =>
        BuildTunConfig(plan, bypass ?? TunBypassOptions.Empty, tunName, mtu, logLevel)
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static JsonObject BuildTunConfig(
        SingBoxConnectPlan plan,
        TunBypassOptions bypass,
        string tunName,
        int mtu,
        string logLevel)
    {
        var bootstrap = plan.Tun.BootstrapDomains.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ruleSets = plan.BypassRuleSets.DistinctBy(rs => rs.Tag).ToList();
        var bypassExes = SelfBypassExes(bypass.BypassExes);
        var bypassHosts = bypass.BypassHosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var experimental = new JsonObject
        {
            ["clash_api"] = new JsonObject
            {
                ["external_controller"] = $"{SingBoxTags.ClashApiHost}:{SingBoxTags.ClashApiPort}",
            },
        };
        if (ruleSets.Count > 0)
            experimental["cache_file"] = new JsonObject { ["enabled"] = true };

        return new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = logLevel, ["timestamp"] = true },
            ["dns"] = MakeDns(plan.Tun.DnsFinal, bootstrap, ruleSets, bypassHosts),
            ["inbounds"] = new JsonArray(MakeTunInbound(tunName, mtu, plan.DirectIPs, plan.Tun.StrictRoute), new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = SingBoxTags.DiagInbound,
                ["listen"] = SingBoxTags.DiagListenHost,
                ["listen_port"] = SingBoxTags.DiagListenPort,
            }),
            ["route"] = MakeRoute(SingBoxTags.TunInbound, plan.DirectIPs, bootstrap, ruleSets, bypassExes, bypassHosts),
            ["outbounds"] = new JsonArray(ToJson(plan.ProxyOutbound), new JsonObject
            {
                ["type"] = "direct",
                ["tag"] = SingBoxTags.Direct,
            }),
            ["experimental"] = experimental,
        };
    }

    private static JsonObject MakeDns(
        string dnsFinal,
        IReadOnlyList<string> bootstrapDomains,
        IReadOnlyList<ApiBypassRuleSet> ruleSets,
        IReadOnlyList<string> bypassHosts)
    {
        var rules = new JsonArray(
            new JsonObject { ["query_type"] = Arr("PTR"), ["action"] = "reject", ["method"] = "drop" },
            new JsonObject
            {
                ["domain"] = Arr("localhost"),
                ["action"] = "predefined",
                ["answer"] = Arr("localhost. IN A 127.0.0.1"),
            });
        foreach (var domain in bootstrapDomains.Concat(bypassHosts).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (CleanedDomain(domain) is not { } clean) continue;
            rules.Add(new JsonObject
            {
                ["domain"] = Arr(clean),
                ["action"] = "route",
                ["server"] = SingBoxTags.DnsDirect,
            });
        }
        foreach (var rs in ruleSets.Where(r => r.IsGeosite))
        {
            rules.Add(new JsonObject
            {
                ["rule_set"] = Arr(rs.Tag),
                ["action"] = "route",
                ["server"] = SingBoxTags.DnsDirect,
            });
        }
        return new JsonObject
        {
            ["servers"] = new JsonArray(
                new JsonObject { ["type"] = "udp", ["tag"] = SingBoxTags.DnsDirect, ["server"] = "1.1.1.1" },
                new JsonObject
                {
                    ["type"] = "https",
                    ["tag"] = SingBoxTags.DnsRemote,
                    ["server"] = "1.1.1.1",
                    ["detour"] = SingBoxTags.Proxy,
                    ["connect_timeout"] = "8s",
                }),
            ["rules"] = rules,
            ["final"] = dnsFinal,
            ["strategy"] = "ipv4_only",
        };
    }

    private static JsonObject MakeRoute(
        string inboundTag,
        IReadOnlyList<string> directIPs,
        IReadOnlyList<string> bootstrapDomains,
        IReadOnlyList<ApiBypassRuleSet> ruleSets,
        IReadOnlyList<string> bypassExes,
        IReadOnlyList<string> bypassHosts)
    {
        var rules = new JsonArray(
            new JsonObject { ["inbound"] = Arr(inboundTag), ["action"] = "sniff" },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
            new JsonObject { ["network"] = Arr("udp"), ["port"] = Arr(443), ["action"] = "reject" },
            new JsonObject { ["ip_cidr"] = Arr("127.0.0.0/8"), ["action"] = "route", ["outbound"] = SingBoxTags.Direct },
            new JsonObject { ["ip_is_private"] = true, ["action"] = "route", ["outbound"] = SingBoxTags.Direct });

        foreach (var exe in bypassExes)
            rules.Add(new JsonObject
            {
                ["process_name"] = Arr(exe),
                ["action"] = "route",
                ["outbound"] = SingBoxTags.Direct,
            });

        foreach (var rs in ruleSets)
            rules.Add(new JsonObject
            {
                ["rule_set"] = Arr(rs.Tag),
                ["action"] = "route",
                ["outbound"] = SingBoxTags.Direct,
            });

        foreach (var ip in directIPs)
            rules.Add(new JsonObject { ["ip_cidr"] = Arr($"{ip}/32"), ["action"] = "route", ["outbound"] = SingBoxTags.Direct });

        foreach (var domain in bypassHosts)
        {
            if (CleanedDomain(domain) is not { } clean) continue;
            rules.Add(new JsonObject { ["domain"] = Arr(clean), ["action"] = "route", ["outbound"] = SingBoxTags.Direct });
        }

        foreach (var domain in bootstrapDomains)
        {
            if (CleanedDomain(domain) is not { } clean) continue;
            rules.Add(new JsonObject { ["domain"] = Arr(clean), ["action"] = "route", ["outbound"] = SingBoxTags.Direct });
        }

        rules.Add(new JsonObject { ["ip_version"] = 6, ["action"] = "reject" });
        rules.Add(new JsonObject { ["inbound"] = Arr(SingBoxTags.DiagInbound), ["action"] = "route", ["outbound"] = SingBoxTags.Proxy });
        rules.Add(new JsonObject { ["inbound"] = Arr(inboundTag), ["action"] = "route", ["outbound"] = SingBoxTags.Proxy });

        var route = new JsonObject
        {
            ["auto_detect_interface"] = true,
            ["default_domain_resolver"] = new JsonObject { ["server"] = SingBoxTags.DnsDirect, ["strategy"] = "ipv4_only" },
            ["final"] = SingBoxTags.Proxy,
            ["rules"] = rules,
        };
        if (ruleSets.Count > 0)
        {
            var sets = new JsonArray();
            foreach (var rs in ruleSets)
            {
                sets.Add(new JsonObject
                {
                    ["tag"] = rs.Tag,
                    ["type"] = "remote",
                    ["format"] = "binary",
                    ["url"] = rs.Url,
                    ["download_detour"] = SingBoxTags.Proxy,
                });
            }
            route["rule_set"] = sets;
        }
        return route;
    }

    private static JsonObject MakeTunInbound(string tunName, int mtu, IReadOnlyList<string> directIPs, bool strictRoute)
    {
        var tun = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = SingBoxTags.TunInbound,
            ["interface_name"] = tunName,
            ["mtu"] = Math.Min(mtu, 1400),
            ["auto_route"] = true,
            ["strict_route"] = strictRoute,
            ["address"] = Arr("172.19.0.1/30"),
            ["stack"] = "system",
            ["udp_timeout"] = "5m",
            ["endpoint_independent_nat"] = false,
        };
        if (directIPs.Count > 0)
            tun["route_address"] = Arr("0.0.0.0/1", "128.0.0.0/1");

        var exclude = new JsonArray();
        foreach (var cidr in TunExcludeCidrs) exclude.Add(cidr);
        foreach (var ip in directIPs) exclude.Add($"{ip}/32");
        tun["route_exclude_address"] = exclude;
        return tun;
    }

    private static List<string> SelfBypassExes(IReadOnlyList<string> bypassExes)
    {
        var self = TunExeNames.Normalize(Environment.ProcessPath);
        if (self.Length == 0) self = TunExeNames.AppExe.ToLowerInvariant();
        return bypassExes
            .Select(TunExeNames.Normalize)
            .Where(x => x.Length > 0)
            .Append(TunExeNames.SingBox)
            .Append(self)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? CleanedDomain(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static JsonObject ToJson(Dictionary<string, object?> map)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in map)
            obj[k] = ToNode(v);
        return obj;
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        bool b => b,
        int i => i,
        long l => l,
        double d => d,
        string s => s,
        Dictionary<string, object?> dict => ToJson(dict),
        IEnumerable<object?> list => ToArray(list),
        _ => JsonValue.Create(value.ToString()),
    };

    private static JsonArray ToArray(IEnumerable<object?> list)
    {
        var arr = new JsonArray();
        foreach (var item in list) arr.Add(ToNode(item));
        return arr;
    }

    private static JsonArray Arr(params object?[] items)
    {
        var arr = new JsonArray();
        foreach (var item in items) arr.Add(ToNode(item));
        return arr;
    }
}
