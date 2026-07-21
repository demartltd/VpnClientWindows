using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Core.Servers;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Network;
using DemarVpn.Services.Vpn;

namespace DemarVpn.Tests;

public class VpnPipelineTest
{
    [Fact]
    public void TestServer_profileToTunConfig()
    {
        foreach (var server in TestServer.All)
        {
            var plan = server.ConnectPlan;
            var json = SingBoxConfigBuilder.BuildTunConfigJson(plan);
            var cfg = JsonNode.Parse(json)!.AsObject();

            Assert.False(string.IsNullOrEmpty(plan.UplinkHost));
            Assert.Contains("\"type\": \"naive\"", json);
            Assert.Contains($"\"server\": \"{plan.UplinkHost}\"", json);
            Assert.Contains("\"type\": \"tun\"", json);
            Assert.Contains("\"action\": \"hijack-dns\"", json);
            Assert.DoesNotContain("override_android_vpn", json);
            Assert.Contains($"\"tag\": \"{SingBoxTags.DiagInbound}\"", json);

            var outbounds = cfg["outbounds"]!.AsArray();
            var tags = outbounds.Select(o => o!["tag"]!.GetValue<string>()).ToList();
            Assert.Contains(SingBoxTags.Proxy, tags);
            Assert.Contains(SingBoxTags.Direct, tags);

            var naive = outbounds[0]!.AsObject();
            Assert.Equal("naive", naive["type"]!.GetValue<string>());
            Assert.Equal("10s", naive["connect_timeout"]!.GetValue<string>());
            Assert.False(naive["quic"]!.GetValue<bool>());
            Assert.True(naive["udp_over_tcp"]!.AsObject()["enabled"]!.GetValue<bool>());
            Assert.Equal(2, naive["udp_over_tcp"]!.AsObject()["version"]!.GetValue<int>());

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
            foreach (var host in new[] { "www.google.com", "www.cloudflare.com" })
            {
                Assert.False(route["rules"]!.AsArray().Any(rule =>
                    rule!["domain"]?.AsArray().Any(d => d!.GetValue<string>() == host) == true));
            }

            var inbounds = cfg["inbounds"]!.AsArray();
            var tun = inbounds.First(i => i!["type"]!.GetValue<string>() == "tun")!.AsObject();
            Assert.Equal(plan.Tun.StrictRoute, tun["strict_route"]!.GetValue<bool>());
            Assert.Equal("system", tun["stack"]!.GetValue<string>());

            var diag = inbounds.First(i => i!["type"]!.GetValue<string>() == "mixed")!.AsObject();
            Assert.Equal(SingBoxTags.DiagListenPort, diag["listen_port"]!.GetValue<int>());
        }
    }

    [Fact]
    public void LocalBypass_rulesPresent()
    {
        var plan = TestServer.Default.ConnectPlan;
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
        var profile = new NaiveProfile("host-test", "vpn.example.com", 443, "u", "p", "tls.example.com");
        var plan = ServerProfile.Naive(profile).ConnectPlan;
        Assert.Contains("vpn.example.com", plan.Tun.BootstrapDomains);
        Assert.Contains("tls.example.com", plan.Tun.BootstrapDomains);
        Assert.Empty(plan.DirectIPs);
    }

    [Fact]
    public void LiteralIpv4Server_hasRouteExclude()
    {
        var plan = ServerProfile.Naive(new NaiveProfile("ipv4-test", "193.35.17.5", 443, "u", "p", "example.com")).ConnectPlan;
        var json = SingBoxConfigBuilder.BuildTunConfigJson(plan);
        Assert.Contains("route_exclude_address", json);
        Assert.NotEmpty(plan.DirectIPs);
    }

    [Fact]
    public void WithExtraDirectIps_mergesBypass()
    {
        var plan = ServerProfile.Naive(new NaiveProfile("ipv4-test", "193.35.17.5", 443, "u", "p", "example.com")).ConnectPlan
            .WithExtraDirectIps(["10.0.0.1"]);
        Assert.Contains(plan.UplinkHost, plan.DirectIPs);
        Assert.Contains("10.0.0.1", plan.DirectIPs);
    }

    [Fact]
    public void IsLiteralIpv4_matchesConnectPlan()
    {
        var plan = ServerProfile.Naive(new NaiveProfile("ipv4-test", "193.35.17.5", 443, "u", "p", "example.com")).ConnectPlan;
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
    [InlineData("FI-DEV-99", "FI")]
    [InlineData("SE-FREE-71", "SE")]
    [InlineData("GR-FREE-22", "GR")]
    [InlineData("DE-FREE-1", "DE")]
    public void NaiveFromTag_parsesCountryCode(string tag, string expectedCode)
    {
        var profile = ServerProfile.NaiveFromTag(new NaiveProfile(tag, "host", 443, "u", "p", "host"));
        Assert.Equal(expectedCode, profile.CountryCode);
        Assert.False(string.IsNullOrEmpty(profile.CountryName));
    }
}
