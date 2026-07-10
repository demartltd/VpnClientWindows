using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Core.Servers;
using DemarVpn.Domain.Vpn;
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
            Assert.Equal("5s", naive["connect_timeout"]!.GetValue<string>());
            Assert.False(naive["quic"]!.GetValue<bool>());
            Assert.True(naive["udp_over_tcp"]!.AsObject()["enabled"]!.GetValue<bool>());
            Assert.Equal(2, naive["udp_over_tcp"]!.AsObject()["version"]!.GetValue<int>());

            Assert.True(cfg["log"]!.AsObject()["timestamp"]!.GetValue<bool>());

            var route = cfg["route"]!.AsObject();
            Assert.Equal(SingBoxTags.Proxy, route["final"]!.GetValue<string>());
            Assert.True(route["rules"]!.AsArray().Any(r => r!["action"]?.GetValue<string>() == "hijack-dns"));

            var dns = cfg["dns"]!.AsObject();
            Assert.Equal(SingBoxTags.DnsRemote, dns["final"]!.GetValue<string>());
            foreach (var domain in plan.Tun.BootstrapDomains)
            {
                Assert.True(dns["rules"]!.AsArray().Any(rule =>
                    rule!["domain"]?.AsArray().Any(d => d!.GetValue<string>() == domain) == true));
            }
            foreach (var host in VpnTunnelProbe.ProbeBootstrapDomains)
            {
                Assert.True(dns["rules"]!.AsArray().Any(rule =>
                    rule!["domain"]?.AsArray().Any(d => d!.GetValue<string>() == host) == true));
                Assert.True(route["rules"]!.AsArray().Any(rule =>
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
