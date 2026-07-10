using DemarVpn.Core.Servers;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Network;

namespace DemarVpn.Tests;

public class UplinkProbeTest
{
    [Fact]
    public async Task TcpReachable_closedPort_returnsFalse()
    {
        Assert.False(await UplinkProbe.TcpReachableAsync("127.0.0.1", 1, timeoutMs: 500));
    }

    [Fact]
    public void ConnectTarget_literalIpv4_returnsHost()
    {
        var plan = ServerProfile.Naive(new NaiveProfile("ipv4-test", "193.35.17.5", 443, "u", "p", "example.com")).ConnectPlan;
        Assert.Equal("193.35.17.5", UplinkProbe.ConnectTarget(plan, []));
    }

    [Fact]
    public void ConnectTarget_resolvedIp_preferred()
    {
        var plan = TestServer.All[1].ConnectPlan;
        Assert.Equal("93.184.216.34", UplinkProbe.ConnectTarget(plan, ["93.184.216.34"]));
    }

    [Fact]
    public void ConnectTarget_unresolvedHostname_fallsBackToHost()
    {
        var plan = TestServer.All[1].ConnectPlan;
        Assert.Equal(plan.UplinkHost, UplinkProbe.ConnectTarget(plan, []));
    }
}
