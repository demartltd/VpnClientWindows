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
        bool adBlock = false,
        string? adsInitialPath = null,
        string tunName = "DemarVPN",
        int mtu = 1492,
        string logLevel = "warning") =>
        BuildTunConfig(plan, bypass ?? TunBypassOptions.Empty, adBlock, adsInitialPath, tunName, mtu, logLevel)
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static JsonObject BuildTunConfig(
        SingBoxConnectPlan plan,
        TunBypassOptions bypass,
        bool adBlock,
        string? adsInitialPath,
        string tunName,
        int mtu,
        string logLevel)
    {
        var bootstrap = CleanedDomains(plan.Tun.BootstrapDomains);
        var ruleSets = plan.BypassRuleSets.DistinctBy(rs => rs.Tag).ToList();
        var bypassExes = SelfBypassExes(bypass.BypassExes);
        var bypassHosts = CleanedDomains(bypass.BypassHosts);
        var cfg = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = logLevel, ["timestamp"] = true },
            ["dns"] = MakeDns(plan.Tun.DnsFinal, bootstrap, ruleSets, bypassExes, bypassHosts, adBlock),
            ["inbounds"] = new JsonArray(MakeTunInbound(tunName, mtu, plan.DirectIPs), new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = SingBoxTags.DiagInbound,
                ["listen"] = SingBoxTags.DiagListenHost,
                ["listen_port"] = SingBoxTags.DiagListenPort,
            }),
            ["route"] = MakeRoute(SingBoxTags.TunInbound, plan.DirectIPs, bootstrap, ruleSets, bypassExes, bypassHosts, adBlock, adsInitialPath),
            ["outbounds"] = new JsonArray(ToJson(plan.ProxyOutbound), new JsonObject
            {
                ["type"] = "direct",
                ["tag"] = SingBoxTags.Direct,
            }),
            ["experimental"] = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = $"{SingBoxTags.ClashApiHost}:{SingBoxTags.ClashApiPort}",
                },
                ["cache_file"] = new JsonObject { ["enabled"] = true, ["store_dns"] = true },
            },
        };
        if (adBlock)
        {
            cfg["http_clients"] = new JsonArray(new JsonObject
            {
                ["tag"] = SingBoxTags.HttpRuleSetDownload,
                ["detour"] = SingBoxTags.Proxy,
            });
        }
        return cfg;
    }

    private static JsonObject MakeDns(
        string dnsFinal,
        IReadOnlyList<string> bootstrap,
        IReadOnlyList<ApiBypassRuleSet> ruleSets,
        IReadOnlyList<string> bypassExes,
        IReadOnlyList<string> bypassHosts,
        bool adBlock)
    {
        var rules = new JsonArray(
            new JsonObject { ["query_type"] = Arr("PTR"), ["action"] = "reject", ["method"] = "drop" },
            new JsonObject
            {
                ["domain"] = Arr("localhost"),
                ["action"] = "predefined",
                ["answer"] = Arr("localhost. IN A 127.0.0.1"),
            });
        if (bypassExes.Count > 0)
            rules.Add(DnsRoute("process_name", bypassExes));
        if (bootstrap.Count > 0)
            rules.Add(DnsRoute("domain", bootstrap));
        if (bypassHosts.Count > 0)
            rules.Add(DnsRoute("domain_suffix", bypassHosts));
        if (adBlock)
            rules.Add(new JsonObject { ["rule_set"] = Arr(SingBoxTags.AdsRuleSetTag), ["action"] = "reject" });
        foreach (var rs in ruleSets.Where(r => r.IsGeosite))
            rules.Add(DnsRoute("rule_set", [rs.Tag]));
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
            ["optimistic"] = true,
        };
    }

    private static JsonObject MakeRoute(
        string inboundTag,
        IReadOnlyList<string> directIPs,
        IReadOnlyList<string> bootstrap,
        IReadOnlyList<ApiBypassRuleSet> ruleSets,
        IReadOnlyList<string> bypassExes,
        IReadOnlyList<string> bypassHosts,
        bool adBlock,
        string? adsInitialPath)
    {
        var rules = new JsonArray();
        if (bypassExes.Count > 0)
            rules.Add(OutRoute("process_name", bypassExes));
        rules.Add(OutRoute("ip_cidr", ["127.0.0.0/8"]));
        rules.Add(new JsonObject { ["ip_is_private"] = true, ["action"] = "route", ["outbound"] = SingBoxTags.Direct });
        foreach (var rs in ruleSets.Where(r => !r.IsGeosite))
            rules.Add(OutRoute("rule_set", [rs.Tag]));
        foreach (var ip in directIPs)
            rules.Add(OutRoute("ip_cidr", [$"{ip}/32"]));

        rules.Add(new JsonObject { ["inbound"] = Arr(inboundTag), ["action"] = "sniff", ["timeout"] = "50ms" });
        rules.Add(new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" });
        rules.Add(new JsonObject { ["network"] = Arr("udp"), ["port"] = Arr(443), ["action"] = "reject", ["method"] = "default" });

        foreach (var rs in ruleSets.Where(r => r.IsGeosite))
            rules.Add(OutRoute("rule_set", [rs.Tag]));
        if (bypassHosts.Count > 0)
            rules.Add(OutRoute("domain_suffix", bypassHosts));
        if (bootstrap.Count > 0)
            rules.Add(OutRoute("domain", bootstrap));

        rules.Add(new JsonObject { ["ip_version"] = 6, ["action"] = "reject", ["method"] = "default" });
        rules.Add(OutRoute("inbound", [SingBoxTags.DiagInbound], SingBoxTags.Proxy));
        rules.Add(OutRoute("inbound", [inboundTag], SingBoxTags.Proxy));

        var route = new JsonObject
        {
            ["auto_detect_interface"] = true,
            ["default_domain_resolver"] = new JsonObject { ["server"] = SingBoxTags.DnsDirect, ["strategy"] = "ipv4_only" },
            ["final"] = SingBoxTags.Proxy,
            ["rules"] = rules,
        };
        if (ruleSets.Count > 0 || adBlock)
        {
            var sets = new JsonArray();
            foreach (var rs in ruleSets)
                sets.Add(RemoteRuleSet(rs.Tag, rs.Url, SingBoxTags.Direct));
            if (adBlock)
            {
                sets.Add(RemoteRuleSet(SingBoxTags.AdsRuleSetTag, SingBoxTags.AdsRuleSetUrl, initialPath: adsInitialPath));
                route["default_http_client"] = SingBoxTags.HttpRuleSetDownload;
            }
            route["rule_set"] = sets;
        }
        return route;
    }

    private static JsonObject MakeTunInbound(string tunName, int mtu, IReadOnlyList<string> directIPs)
    {
        var exclude = new JsonArray();
        foreach (var cidr in TunExcludeCidrs) exclude.Add(cidr);
        foreach (var ip in directIPs) exclude.Add($"{ip}/32");
        return new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = SingBoxTags.TunInbound,
            ["interface_name"] = tunName,
            ["mtu"] = Math.Min(mtu, 1400),
            ["auto_route"] = true,
            ["strict_route"] = true,
            ["address"] = Arr("172.19.0.1/30"),
            ["stack"] = "system",
            ["udp_timeout"] = "5m",
            ["endpoint_independent_nat"] = false,
            ["route_address"] = Arr("0.0.0.0/1", "128.0.0.0/1"),
            ["route_exclude_address"] = exclude,
        };
    }

    private static JsonObject DnsRoute(string key, IEnumerable<object?> vals) => new()
    {
        [key] = Arr(vals),
        ["action"] = "route",
        ["server"] = SingBoxTags.DnsDirect,
    };

    private static JsonObject OutRoute(string key, IEnumerable<object?> vals, string outbound = SingBoxTags.Direct) => new()
    {
        [key] = Arr(vals),
        ["action"] = "route",
        ["outbound"] = outbound,
    };

    private static JsonObject RemoteRuleSet(string tag, string url, string? downloadDetour = null, string? initialPath = null)
    {
        var o = new JsonObject
        {
            ["tag"] = tag,
            ["type"] = "remote",
            ["format"] = "binary",
            ["url"] = url,
        };
        if (downloadDetour is not null) o["download_detour"] = downloadDetour;
        if (!string.IsNullOrWhiteSpace(initialPath)) o["initial_path"] = initialPath;
        return o;
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

    private static List<string> CleanedDomains(IEnumerable<string> raw) =>
        raw.Select(s => s.Trim().Trim('.'))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static JsonObject ToJson(Dictionary<string, object?> map)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in map) obj[k] = ToNode(v);
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
        IEnumerable<object?> list => Arr(list),
        _ => JsonValue.Create(value.ToString()),
    };

    private static JsonArray Arr(params object?[] items) => Arr((IEnumerable<object?>)items);

    private static JsonArray Arr(IEnumerable<object?> items)
    {
        var arr = new JsonArray();
        foreach (var item in items) arr.Add(ToNode(item));
        return arr;
    }
}
