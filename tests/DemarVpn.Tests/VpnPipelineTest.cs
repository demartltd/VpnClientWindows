using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services.Network;
using DemarVpn.Services.Servers;
using DemarVpn.Services.Vpn;

namespace DemarVpn.Tests;

public class VpnPipelineTest
{
    private static ServerProfile Fixture(
        string tag = "FI-DEV-99",
        string server = "hy2.example.com",
        int port = 443,
        string? obfs = "obfs-secret",
        string? city = "Helsinki") =>
        ServerProfile.Hysteria2FromTag(
            new Hysteria2Profile(tag, server, port, "pass", server, ["h3"], obfs),
            city: city,
            id: $"id:{tag}");

    [Fact]
    public void Hysteria2_profileToTunConfig()
    {
        var server = Fixture();
        var plan = server.ConnectPlan;
        var json = SingBoxConfigBuilder.BuildTunConfigJson(plan);
        var cfg = JsonNode.Parse(json)!.AsObject();

        Assert.False(string.IsNullOrEmpty(plan.UplinkHost));
        Assert.Contains("\"type\": \"hysteria2\"", json);
        Assert.Contains($"\"server\": \"{plan.UplinkHost}\"", json);
        Assert.Contains("\"type\": \"tun\"", json);
        Assert.Contains("\"action\": \"hijack-dns\"", json);
        Assert.DoesNotContain("override_android_vpn", json);
        Assert.Contains($"\"tag\": \"{SingBoxTags.DiagInbound}\"", json);

        var outbounds = cfg["outbounds"]!.AsArray();
        var tags = outbounds.Select(o => o!["tag"]!.GetValue<string>()).ToList();
        Assert.Contains(SingBoxTags.Proxy, tags);
        Assert.Contains(SingBoxTags.Direct, tags);

        var hy2 = outbounds[0]!.AsObject();
        Assert.Equal("hysteria2", hy2["type"]!.GetValue<string>());
        Assert.Equal("pass", hy2["password"]!.GetValue<string>());
        Assert.Equal("h3", hy2["tls"]!["alpn"]![0]!.GetValue<string>());
        Assert.Equal("salamander", hy2["obfs"]!["type"]!.GetValue<string>());
        Assert.True(hy2["udp_fragment"]!.GetValue<bool>());

        Assert.True(cfg["log"]!.AsObject()["timestamp"]!.GetValue<bool>());

        var route = cfg["route"]!.AsObject();
        Assert.Equal(SingBoxTags.Proxy, route["final"]!.GetValue<string>());
        Assert.True(route["rules"]!.AsArray().Any(r => r!["action"]?.GetValue<string>() == "hijack-dns"));
        Assert.Contains(route["rules"]!.AsArray(), r =>
            r!["action"]?.GetValue<string>() == "sniff" &&
            r!["timeout"]?.GetValue<string>() == "300ms");
        Assert.Contains(route["rules"]!.AsArray(), r =>
            r!["action"]?.GetValue<string>() == "reject" &&
            r!["method"]?.GetValue<string>() == "default" &&
            r!["network"]?.AsArray().Any(n => n!.GetValue<string>() == "udp") == true &&
            r!["port"]?.AsArray().Any(p => p!.GetValue<int>() == 443) == true);

        var dns = cfg["dns"]!.AsObject();
        Assert.Equal(SingBoxTags.DnsRemote, dns["final"]!.GetValue<string>());
        Assert.True(dns["optimistic"]!.GetValue<bool>());
        Assert.True(dns["reverse_mapping"]!.GetValue<bool>());
        var dnsRemote = dns["servers"]!.AsArray()
            .First(s => s!["tag"]!.GetValue<string>() == SingBoxTags.DnsRemote)!.AsObject();
        Assert.Equal("8s", dnsRemote["connect_timeout"]!.GetValue<string>());

        foreach (var domain in plan.Tun.BootstrapDomains)
        {
            Assert.True(dns["rules"]!.AsArray().Any(rule =>
                rule!["domain"]?.AsArray().Any(d => d!.GetValue<string>() == domain) == true));
        }

        var inbounds = cfg["inbounds"]!.AsArray();
        var tun = inbounds.First(i => i!["type"]!.GetValue<string>() == "tun")!.AsObject();
        Assert.True(tun["strict_route"]!.GetValue<bool>());
        Assert.Equal("system", tun["stack"]!.GetValue<string>());

        var diag = inbounds.First(i => i!["type"]!.GetValue<string>() == "mixed")!.AsObject();
        Assert.Equal(SingBoxTags.DiagListenPort, diag["listen_port"]!.GetValue<int>());

        var clash = cfg["experimental"]!["clash_api"]!.AsObject();
        Assert.Equal(
            $"{SingBoxTags.ClashApiHost}:{SingBoxTags.ClashApiPort}",
            clash["external_controller"]!.GetValue<string>());
        var cache = cfg["experimental"]!["cache_file"]!.AsObject();
        Assert.True(cache["enabled"]!.GetValue<bool>());
        Assert.True(cache["store_dns"]!.GetValue<bool>());
    }

    [Fact]
    public void ClashTraffic_parsesSampleLine()
    {
        var sample = ClashTrafficMonitor.TryParse("""{"up":100,"down":200}""");
        Assert.NotNull(sample);
        Assert.Equal(100, sample.Value.Up);
        Assert.Equal(200, sample.Value.Down);
        Assert.Null(ClashTrafficMonitor.TryParse("not-json"));
    }

    [Fact]
    public void LocalBypass_rulesPresent()
    {
        var plan = Fixture().ConnectPlan;
        var cfg = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan))!.AsObject();

        var exclude = cfg["inbounds"]!.AsArray()
            .First(i => i!["type"]!.GetValue<string>() == "tun")!["route_exclude_address"]!.AsArray()
            .Select(x => x!.GetValue<string>()).ToList();
        Assert.Contains("127.0.0.0/8", exclude);
        Assert.Contains("192.168.0.0/16", exclude);

        var routeRules = cfg["route"]!["rules"]!.AsArray();
        Assert.Contains(routeRules, r =>
            r!["ip_cidr"]?.AsArray().Any(c => c!.GetValue<string>() == "127.0.0.0/8") == true &&
            r!["outbound"]?.GetValue<string>() == SingBoxTags.Direct);

        var dnsRules = cfg["dns"]!["rules"]!.AsArray();
        Assert.Contains(dnsRules, r =>
            r!["action"]?.GetValue<string>() == "predefined" &&
            r!["domain"]?.AsArray().Any(d => d!.GetValue<string>() == "localhost") == true);
    }

    [Fact]
    public void HostnameServer_addsBootstrapDomains()
    {
        var plan = Fixture(server: "vpn.example.com").ConnectPlan;
        Assert.Contains("vpn.example.com", plan.Tun.BootstrapDomains);
        Assert.Empty(plan.DirectIPs);
    }

    [Fact]
    public void TunBypass_addsProcessAndDomainRules()
    {
        var plan = Fixture().ConnectPlan;
        var bypass = new TunBypassOptions(["steam.exe"], ["store.steampowered.com"]);
        var cfg = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan, bypass))!.AsObject();
        var rules = cfg["route"]!["rules"]!.AsArray();

        var process = Assert.Single(rules, r => r!["process_name"] is not null);
        var names = process!["process_name"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();
        Assert.Contains("steam.exe", names);
        Assert.Contains(TunExeNames.SingBox, names);
        Assert.Equal(SingBoxTags.Direct, process["outbound"]!.GetValue<string>());

        Assert.Contains(rules, r =>
            r!["domain_suffix"]?.AsArray().Any(d => d!.GetValue<string>() == "store.steampowered.com") == true &&
            r!["outbound"]?.GetValue<string>() == SingBoxTags.Direct);

        var dnsRules = cfg["dns"]!["rules"]!.AsArray();
        Assert.Contains(dnsRules, r =>
            r!["process_name"]?.AsArray().Any(p => p!.GetValue<string>() == "steam.exe") == true &&
            r!["server"]?.GetValue<string>() == SingBoxTags.DnsDirect);
        Assert.Contains(dnsRules, r =>
            r!["domain_suffix"]?.AsArray().Any(d => d!.GetValue<string>() == "store.steampowered.com") == true &&
            r!["server"]?.GetValue<string>() == SingBoxTags.DnsDirect);
    }

    [Fact]
    public void TunBypass_processRuleBeforeHijackAndQuicReject()
    {
        var plan = Fixture().ConnectPlan;
        var bypass = new TunBypassOptions(["steam.exe"], []);
        var rules = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan, bypass))!["route"]!["rules"]!.AsArray();

        var processIdx = IndexOf(rules, r => r!["process_name"] is not null);
        var privateIdx = IndexOf(rules, r => r!["ip_is_private"]?.GetValue<bool>() == true);
        var sniffIdx = IndexOf(rules, r => r!["action"]?.GetValue<string>() == "sniff");
        var hijackIdx = IndexOf(rules, r => r!["action"]?.GetValue<string>() == "hijack-dns");
        var quicIdx = IndexOf(rules, r =>
            r!["action"]?.GetValue<string>() == "reject" &&
            r!["network"]?.AsArray().Any(n => n!.GetValue<string>() == "udp") == true &&
            r!["port"]?.AsArray().Any(p => p!.GetValue<int>() == 443) == true);

        Assert.True(processIdx < privateIdx && privateIdx < sniffIdx && sniffIdx < hijackIdx && hijackIdx < quicIdx);
        Assert.Equal("300ms", rules[sniffIdx]!["timeout"]!.GetValue<string>());
    }

    [Fact]
    public void TunBypass_emptyUserList_selfProcessStillBeforeQuic()
    {
        var plan = Fixture().ConnectPlan;
        var rules = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan))!["route"]!["rules"]!.AsArray();

        var process = Assert.Single(rules, r => r!["process_name"] is not null);
        Assert.Contains(
            process!["process_name"]!.AsArray().Select(p => p!.GetValue<string>()),
            n => n.Equals(TunExeNames.SingBox, StringComparison.OrdinalIgnoreCase));

        var processIdx = IndexOf(rules, r => r!["process_name"] is not null);
        var quicIdx = IndexOf(rules, r =>
            r!["action"]?.GetValue<string>() == "reject" &&
            r!["network"]?.AsArray().Any(n => n!.GetValue<string>() == "udp") == true &&
            r!["port"]?.AsArray().Any(p => p!.GetValue<int>() == 443) == true);
        Assert.True(processIdx < quicIdx);
    }

    private static int IndexOf(JsonArray rules, Func<JsonNode?, bool> pred)
    {
        for (var i = 0; i < rules.Count; i++)
            if (pred(rules[i])) return i;
        return -1;
    }

    [Fact]
    public void LiteralIpv4Server_hasRouteExclude()
    {
        var plan = Fixture(server: "193.35.17.5").ConnectPlan;
        var json = SingBoxConfigBuilder.BuildTunConfigJson(plan);
        Assert.Contains("route_exclude_address", json);
        Assert.NotEmpty(plan.DirectIPs);
    }

    [Fact]
    public void WithExtraDirectIps_mergesBypass()
    {
        var plan = Fixture(server: "193.35.17.5").ConnectPlan.WithExtraDirectIps(["10.0.0.1"]);
        Assert.Contains(plan.UplinkHost, plan.DirectIPs);
        Assert.Contains("10.0.0.1", plan.DirectIPs);
    }

    [Fact]
    public void IsLiteralIpv4_matchesConnectPlan()
    {
        var plan = Fixture(server: "193.35.17.5").ConnectPlan;
        Assert.Equal(["193.35.17.5"], plan.DirectIPs);
        Assert.True(HostUtils.IsLiteralIpv4("193.35.17.5"));
    }

    [Fact]
    public void ConnectTimeouts_healthBudget()
    {
        Assert.Equal(37_000, ConnectTimeouts.WatchdogMs);
        Assert.Equal(ConnectTimeouts.ConnectMs + 2_000, ConnectTimeouts.WatchdogMs);
        Assert.Equal(10_000, ConnectTimeouts.StartupMs);
        Assert.Equal(100, ConnectTimeouts.StartupPollMs);
        Assert.Equal(3_000, ConnectTimeouts.ProbeMs);
        Assert.Equal(900, ConnectTimeouts.ProbeAttemptMs);
        Assert.Equal(25_000, ConnectTimeouts.HealthIntervalMs);
        Assert.Equal(2, ConnectTimeouts.HealthFailLimit);
        Assert.Equal(1_200, ConnectTimeouts.DiagPortCloseMs);
        Assert.Equal(100, ConnectTimeouts.SwitchSettleMs);
    }

    [Fact]
    public void HostnameServer_alwaysEmitsRouteAddressSplit()
    {
        var plan = Fixture(server: "vpn.example.com").ConnectPlan;
        Assert.Empty(plan.DirectIPs);
        var tun = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan))!["inbounds"]!.AsArray()
            .First(i => i!["type"]!.GetValue<string>() == "tun")!.AsObject();
        var routes = tun["route_address"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Equal(["0.0.0.0/1", "128.0.0.0/1"], routes);
    }

    [Fact]
    public void ParseCloudflareTrace_acceptsValidBody()
    {
        const string body = "fl=abc\nh=1.1.1.1\nip=203.0.113.1\nloc=US\n";
        var info = PublicEgressDiagnostics.ParseCloudflareTrace(body);
        Assert.NotNull(info);
        Assert.Equal("203.0.113.1", info!.Ip);
        Assert.Equal("United States", info.Country);
    }

    [Theory]
    [InlineData("FI-DEV-99", "FI", ServerTier.Dev)]
    [InlineData("NL-TESTER-2", "NL", ServerTier.Tester)]
    [InlineData("SE-FREE-71", "SE", ServerTier.Free)]
    [InlineData("GR-FREE-22", "GR", ServerTier.Free)]
    [InlineData("DE-FREE-1", "DE", ServerTier.Free)]
    public void Hysteria2FromTag_parsesCountryCode(string tag, string expectedCode, ServerTier expectedTier)
    {
        var profile = Fixture(tag: tag);
        Assert.Equal(expectedCode, profile.CountryCode);
        Assert.False(string.IsNullOrEmpty(profile.CountryName));
        Assert.Equal(expectedTier, profile.Tier);
    }

    [Fact]
    public void ListTitle_and_ListSubtitle_fromTag()
    {
        var profile = Fixture("FI-DEV-99", city: "Helsinki");
        Assert.Equal("Finland | DEV", profile.ListTitle);
        Assert.Equal("Helsinki #99", profile.ListSubtitle);
        Assert.Equal(99, profile.TagNumber);
    }

    [Fact]
    public void Filter_byProtocolAndTier()
    {
        var servers = new[]
        {
            Fixture("FI-FREE-1"),
            Fixture("FI-DEV-2"),
            Fixture("SE-PLUS-3"),
        };
        var free = ServerProfile.Filter(servers, ServerProtocol.Hysteria2, ServerTier.Free);
        Assert.Single(free);
        Assert.Equal("FI-FREE-1", free[0].DisplayName);

        var all = ServerProfile.Filter(servers, ServerProtocol.Hysteria2, null);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void VisibleTiers_hidesDevAndTesterWhenAbsent()
    {
        var productOnly = new[] { Fixture("FI-FREE-1"), Fixture("SE-PLUS-2") };
        Assert.DoesNotContain(ServerTier.Dev, ServerProfile.VisibleTiers(productOnly));
        Assert.DoesNotContain(ServerTier.Tester, ServerProfile.VisibleTiers(productOnly));
        Assert.Equal(
            new[] { ServerTier.Free, ServerTier.Plus, ServerTier.Vip },
            ServerProfile.VisibleTiers(productOnly));
        Assert.Contains(ServerTier.Dev, ServerProfile.VisibleTiers([Fixture("FI-DEV-1")]));
        Assert.Contains(ServerTier.Tester, ServerProfile.VisibleTiers([Fixture("NL-TESTER-1")]));
    }

    [Fact]
    public void ServerTier_requiresUpgrade()
    {
        Assert.True(ServerTier.Plus.RequiresUpgrade("free"));
        Assert.True(ServerTier.Vip.RequiresUpgrade("plus"));
        Assert.False(ServerTier.Plus.RequiresUpgrade("vip"));
        Assert.False(ServerTier.Free.RequiresUpgrade(null));
    }

    [Fact]
    public void ServersApi_parseHysteria2_andSkipUnsupported()
    {
        var hy2 = JsonNode.Parse("""
            {
              "id": "s1",
              "name": "FI-FREE-1",
              "city": "Helsinki",
              "protocol": "hysteria2",
              "clientConfigJson": "{\"outbounds\":[{\"type\":\"hysteria2\",\"server\":\"1.2.3.4\",\"server_port\":443,\"password\":\"x\",\"tls\":{\"server_name\":\"1.2.3.4\",\"alpn\":[\"h3\"]}}]}"
            }
            """)!.AsObject();
        var profile = ServersApi.ParseServerItem(hy2);
        Assert.NotNull(profile);
        Assert.Equal("s1", profile!.Id);
        Assert.Equal(443, profile.UplinkPort);
        Assert.Equal("Finland | FREE", profile.ListTitle);
        Assert.Equal("Helsinki #1", profile.ListSubtitle);
        Assert.Null(profile.Profile.UpMbps);
        Assert.Null(profile.Profile.DownMbps);
        Assert.Empty(profile.BypassRuleSets ?? []);

        var skip = JsonNode.Parse("""
            {"id":"s2","name":"X","protocol":"trojan","clientConfigJson":"{}"}
            """)!.AsObject();
        Assert.Null(ServersApi.ParseServerItem(skip));
    }

    [Fact]
    public void ServersApi_previewWithoutClientConfig()
    {
        var item = JsonNode.Parse("""
            {"id":"s3","name":"DE-PLUS-1","city":"Berlin","protocol":"hysteria2"}
            """)!.AsObject();
        var profile = ServersApi.ParseServerItem(item);
        Assert.NotNull(profile);
        Assert.Equal("s3", profile!.Id);
        Assert.Equal("DE-PLUS-1", profile.DisplayName);
        Assert.Equal("", profile.Profile.Password);
        Assert.Equal("Germany | PLUS", profile.ListTitle);
    }

    [Fact]
    public void ServersApi_parseBandwidthAndRuleSets()
    {
        var config = """
            {
              "outbounds": [
                {
                  "type": "hysteria2",
                  "tag": "FI-FREE-2",
                  "city": "Helsinki",
                  "server": "hy2.example.com",
                  "server_port": 443,
                  "password": "hy2-pass",
                  "up_mbps": 10,
                  "down_mbps": 10,
                  "tls": { "enabled": true, "server_name": "hy2.example.com", "alpn": ["h3"] }
                },
                { "type": "direct", "tag": "direct" }
              ],
              "route": {
                "final": "FI-FREE-2",
                "rule_set": [
                  {
                    "tag": "geoip-ru",
                    "type": "remote",
                    "format": "binary",
                    "url": "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-ru.srs"
                  },
                  {
                    "tag": "geosite-category-ru",
                    "type": "remote",
                    "format": "binary",
                    "url": "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ru.srs"
                  },
                  {
                    "tag": "geoip-ir",
                    "type": "remote",
                    "format": "binary",
                    "url": "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-ir.srs"
                  },
                  {
                    "tag": "geosite-ir",
                    "type": "remote",
                    "format": "binary",
                    "url": "https://raw.githubusercontent.com/Chocolate4U/Iran-sing-box-rules/rule-set/geosite-ir.srs"
                  },
                  {
                    "tag": "geoip-cn",
                    "type": "remote",
                    "format": "binary",
                    "url": "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-cn.srs"
                  },
                  {
                    "tag": "geosite-cn",
                    "type": "remote",
                    "format": "binary",
                    "url": "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-cn.srs"
                  }
                ]
              }
            }
            """;
        var item = new JsonObject
        {
            ["id"] = "s2",
            ["name"] = "FI-FREE-2",
            ["protocol"] = "hysteria2",
            ["clientConfigJson"] = config,
        };

        var profile = ServersApi.ParseServerItem(item);
        Assert.NotNull(profile);
        Assert.Equal(10, profile!.Profile.UpMbps);
        Assert.Equal(10, profile.Profile.DownMbps);
        Assert.Equal(6, profile.BypassRuleSets!.Count);
        Assert.Contains(profile.BypassRuleSets, rs => rs.Tag == "geosite-ir" && rs.Url.Contains("Chocolate4U"));
        Assert.Contains(profile.BypassRuleSets, rs => rs.Tag == "geosite-cn");

        var built = SingBoxConfigBuilder.BuildTunConfigJson(profile.ConnectPlan);
        Assert.Contains("\"up_mbps\": 10", built);
        Assert.Contains("\"down_mbps\": 10", built);

        var cfg = JsonNode.Parse(built)!.AsObject();
        Assert.True(cfg["route"]!.AsObject().ContainsKey("rule_set"));
        Assert.True(cfg["experimental"]!["cache_file"]!["enabled"]!.GetValue<bool>());
        Assert.True(cfg["experimental"]!["cache_file"]!["store_dns"]!.GetValue<bool>());
        Assert.Contains(
            cfg["dns"]!["rules"]!.AsArray(),
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == "geosite-cn") == true &&
                 r!["server"]?.GetValue<string>() == SingBoxTags.DnsDirect);
        Assert.Contains(
            cfg["route"]!["rules"]!.AsArray(),
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == "geoip-ru") == true &&
                 r!["outbound"]?.GetValue<string>() == SingBoxTags.Direct);
        var routeRules = cfg["route"]!["rules"]!.AsArray();
        var sniffIdx = IndexOf(routeRules, r => r!["action"]?.GetValue<string>() == "sniff");
        var geoipIdx = IndexOf(routeRules, r =>
            r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == "geoip-ru") == true);
        var geositeIdx = IndexOf(routeRules, r =>
            r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == "geosite-cn") == true);
        Assert.True(geoipIdx < sniffIdx);
        Assert.True(sniffIdx < geositeIdx);
        Assert.All(
            cfg["route"]!["rule_set"]!.AsArray(),
            rs => Assert.Equal(SingBoxTags.Direct, rs!["download_detour"]!.GetValue<string>()));
    }

    [Fact]
    public void Bandwidth_omittedWhenMissingOrNonPositive()
    {
        var zero = JsonNode.Parse("""
            {
              "id": "z1",
              "name": "FI-FREE-1",
              "protocol": "hysteria2",
              "clientConfigJson": "{\"outbounds\":[{\"type\":\"hysteria2\",\"server\":\"1.2.3.4\",\"server_port\":443,\"password\":\"x\",\"up_mbps\":0,\"down_mbps\":-1,\"tls\":{\"server_name\":\"1.2.3.4\"}}]}"
            }
            """)!.AsObject();
        var profile = ServersApi.ParseServerItem(zero);
        Assert.NotNull(profile);
        Assert.Null(profile!.Profile.UpMbps);
        Assert.Null(profile.Profile.DownMbps);
        var plan = profile.ConnectPlan;
        Assert.False(plan.ProxyOutbound.ContainsKey("up_mbps"));
        Assert.False(plan.ProxyOutbound.ContainsKey("down_mbps"));
    }

    [Fact]
    public void RuleSets_empty_omitsRouteRuleSet_keepsCacheFile()
    {
        var plan = Fixture().ConnectPlan;
        Assert.Empty(plan.BypassRuleSets);
        var cfg = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan))!.AsObject();
        Assert.False(cfg["route"]!.AsObject().ContainsKey("rule_set"));
        var cache = cfg["experimental"]!["cache_file"]!.AsObject();
        Assert.True(cache["enabled"]!.GetValue<bool>());
        Assert.True(cache["store_dns"]!.GetValue<bool>());
        Assert.True(cfg["dns"]!["optimistic"]!.GetValue<bool>());
        Assert.True(cfg["dns"]!["reverse_mapping"]!.GetValue<bool>());
    }

    [Fact]
    public void Bandwidth_emittedWhenPositive()
    {
        var plan = SingBoxConnectPlan.Hysteria2(
            new Hysteria2Profile("FI-FREE-1", "1.2.3.4", 443, "x", "1.2.3.4", UpMbps: 10, DownMbps: 10));
        Assert.Equal(10, plan.ProxyOutbound["up_mbps"]);
        Assert.Equal(10, plan.ProxyOutbound["down_mbps"]);
        var json = SingBoxConfigBuilder.BuildTunConfigJson(plan);
        Assert.Contains("\"up_mbps\": 10", json);
        Assert.Contains("\"down_mbps\": 10", json);
    }

    [Fact]
    public void AdBlock_localRuleSetDnsAndRouteReject()
    {
        var plan = Fixture().ConnectPlan;
        const string seed = @"C:\tmp\geosite-category-ads.srs";
        var on = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan, adsPath: seed))!.AsObject();

        Assert.Contains(
            on["dns"]!["rules"]!.AsArray(),
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == SingBoxTags.AdsRuleSetTag) == true &&
                 r!["action"]?.GetValue<string>() == "reject" &&
                 r!["no_drop"]?.GetValue<bool>() == true);
        var routeRules = on["route"]!["rules"]!.AsArray();
        Assert.Contains(
            routeRules,
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == SingBoxTags.AdsRuleSetTag) == true &&
                 r!["action"]?.GetValue<string>() == "reject" &&
                 r!["no_drop"]?.GetValue<bool>() == true);
        var sniffIdx = IndexOf(routeRules, r => r!["action"]?.GetValue<string>() == "sniff");
        var adsIdx = IndexOf(routeRules, r =>
            r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == SingBoxTags.AdsRuleSetTag) == true &&
            r!["action"]?.GetValue<string>() == "reject");
        Assert.True(sniffIdx >= 0 && adsIdx > sniffIdx);
        Assert.Contains(
            on["route"]!["rule_set"]!.AsArray(),
            rs => rs!["tag"]!.GetValue<string>() == SingBoxTags.AdsRuleSetTag &&
                  rs!["type"]!.GetValue<string>() == "local" &&
                  rs!["path"]!.GetValue<string>() == seed &&
                  rs!["url"] is null);
        Assert.False(on.ContainsKey("http_clients"));
        Assert.DoesNotContain(
            routeRules,
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == SingBoxTags.AdsRuleSetTag) == true &&
                 r!["outbound"]?.GetValue<string>() == SingBoxTags.Direct);

        var off = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan))!.AsObject();
        Assert.DoesNotContain(
            off["dns"]!["rules"]!.AsArray(),
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == SingBoxTags.AdsRuleSetTag) == true);
        Assert.False(off["route"]!.AsObject().ContainsKey("rule_set"));
    }

    [Fact]
    public void AdBlock_blankAdsPath_omitsAds()
    {
        var plan = Fixture().ConnectPlan;
        foreach (var blank in new[] { null, "", "  " })
        {
            var cfg = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan, adsPath: blank))!.AsObject();
            Assert.DoesNotContain(
                cfg["dns"]!["rules"]!.AsArray(),
                r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == SingBoxTags.AdsRuleSetTag) == true);
            Assert.False(cfg["route"]!.AsObject().ContainsKey("rule_set"));
        }
    }

    [Fact]
    public void AdBlock_withGeosite_keepsAdsAndBypass()
    {
        var plan = SingBoxConnectPlan.Hysteria2(
            Fixture().Profile,
            [
                new ApiBypassRuleSet(
                    "geosite-category-ru",
                    "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ru.srs"),
            ]);
        const string seed = @"C:\tmp\geosite-category-ads.srs";
        var cfg = JsonNode.Parse(SingBoxConfigBuilder.BuildTunConfigJson(plan, adsPath: seed))!.AsObject();
        var sets = cfg["route"]!["rule_set"]!.AsArray();
        Assert.Contains(sets, rs => rs!["tag"]!.GetValue<string>() == SingBoxTags.AdsRuleSetTag && rs!["type"]!.GetValue<string>() == "local");
        Assert.Contains(sets, rs => rs!["tag"]!.GetValue<string>() == "geosite-category-ru" && rs!["type"]!.GetValue<string>() == "remote");
        Assert.Contains(
            cfg["dns"]!["rules"]!.AsArray(),
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == SingBoxTags.AdsRuleSetTag) == true &&
                 r!["action"]?.GetValue<string>() == "reject");
        Assert.Contains(
            cfg["dns"]!["rules"]!.AsArray(),
            r => r!["rule_set"]?.AsArray().Any(t => t!.GetValue<string>() == "geosite-category-ru") == true &&
                 r!["server"]?.GetValue<string>() == SingBoxTags.DnsDirect);
    }

    [Fact]
    public void AdBlock_effectiveGate()
    {
        var paid = new TunSettings([], new Dictionary<string, bool>(), new Dictionary<string, bool>());
        Assert.True(paid.IsAdBlockEnabled(false));
        Assert.False(paid.IsAdBlockEnabled(true));
        Assert.False((paid with { AdBlockEnabled = false }).IsAdBlockEnabled(false));
        Assert.True((paid with { AdBlockEnabled = true }).IsAdBlockEnabled(false));
    }
}
