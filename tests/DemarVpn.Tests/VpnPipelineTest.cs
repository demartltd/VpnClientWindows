using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
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

        Assert.True(cfg["log"]!.AsObject()["timestamp"]!.GetValue<bool>());

        var route = cfg["route"]!.AsObject();
        Assert.Equal(SingBoxTags.Proxy, route["final"]!.GetValue<string>());
        Assert.True(route["rules"]!.AsArray().Any(r => r!["action"]?.GetValue<string>() == "hijack-dns"));

        var dns = cfg["dns"]!.AsObject();
        Assert.Equal(SingBoxTags.DnsRemote, dns["final"]!.GetValue<string>());
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
        Assert.Equal(plan.Tun.StrictRoute, tun["strict_route"]!.GetValue<bool>());
        Assert.Equal("system", tun["stack"]!.GetValue<string>());

        var diag = inbounds.First(i => i!["type"]!.GetValue<string>() == "mixed")!.AsObject();
        Assert.Equal(SingBoxTags.DiagListenPort, diag["listen_port"]!.GetValue<int>());
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
        Assert.Equal(42_000, ConnectTimeouts.WatchdogMs);
        Assert.Equal(ConnectTimeouts.ConnectMs + 2_000, ConnectTimeouts.WatchdogMs);
        Assert.Equal(25_000, ConnectTimeouts.HealthIntervalMs);
        Assert.Equal(2, ConnectTimeouts.HealthFailLimit);
        Assert.Equal(3_000, ConnectTimeouts.PreflightMs);
        Assert.Equal(6_000, ConnectTimeouts.PreflightRaceMs);
        Assert.Equal(2_000, ConnectTimeouts.ProxyWarmupMs);
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

        var skip = JsonNode.Parse("""
            {"id":"s2","name":"X","protocol":"trojan","clientConfigJson":"{}"}
            """)!.AsObject();
        Assert.Null(ServersApi.ParseServerItem(skip));
    }
}
