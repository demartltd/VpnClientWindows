using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Network;

namespace DemarVpn.Tests;

public class UplinkProbeTest
{
    private static SingBoxConnectPlan Plan(string server) =>
        ServerProfile.Hysteria2(new Hysteria2Profile("t", server, 443, "p", "sni")).ConnectPlan;

    [Fact]
    public async Task TcpReachable_closedPort_returnsFalse()
    {
        Assert.False(await UplinkProbe.TcpReachableAsync("127.0.0.1", 1, timeoutMs: 500));
    }

    [Fact]
    public void ConnectTarget_literalIpv4_returnsHost()
    {
        Assert.Equal("193.35.17.5", UplinkProbe.ConnectTarget(Plan("193.35.17.5"), []));
    }

    [Fact]
    public void ConnectTarget_resolvedIp_preferred()
    {
        Assert.Equal("93.184.216.34", UplinkProbe.ConnectTarget(Plan("host.example"), ["93.184.216.34"]));
    }

    [Fact]
    public void ConnectTarget_unresolvedHostname_fallsBackToHost()
    {
        var plan = Plan("host.example");
        Assert.Equal(plan.UplinkHost, UplinkProbe.ConnectTarget(plan, []));
    }
}
