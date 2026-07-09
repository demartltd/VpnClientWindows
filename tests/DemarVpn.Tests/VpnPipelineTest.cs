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
            Assert.False(naive["quic"]!.GetValue<bool>());
            Assert.True(naive["udp_over_tcp"]!.AsObject()["enabled"]!.GetValue<bool>());

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
        var plan = ServerProfile.FromNaive(profile).ConnectPlan;
        Assert.Contains("vpn.example.com", plan.Tun.BootstrapDomains);
        Assert.Contains("tls.example.com", plan.Tun.BootstrapDomains);
        Assert.Empty(plan.DirectIPs);
    }

    [Fact]
    public void LiteralIpv4Server_hasRouteExclude()
    {
        var plan = TestServer.All[0].ConnectPlan;
        var json = SingBoxConfigBuilder.BuildTunConfigJson(plan);
        Assert.Contains("route_exclude_address", json);
        Assert.NotEmpty(plan.DirectIPs);
    }

    [Fact]
    public void WithExtraDirectIps_mergesBypass()
    {
        var plan = TestServer.All[0].ConnectPlan.WithExtraDirectIps(["10.0.0.1"]);
        Assert.Contains(plan.UplinkHost, plan.DirectIPs);
        Assert.Contains("10.0.0.1", plan.DirectIPs);
    }

    [Fact]
    public void IsLiteralIpv4_matchesConnectPlan()
    {
        Assert.Equal(["193.35.17.5"], TestServer.All[0].ConnectPlan.DirectIPs);
        Assert.True(HostUtils.IsLiteralIpv4("193.35.17.5"));
    }
}
