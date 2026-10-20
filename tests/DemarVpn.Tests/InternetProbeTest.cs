using System.Net;
using System.Net.Sockets;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Network;

namespace DemarVpn.Tests;

public class InternetProbeTest
{
    [Fact]
    public void QuicGreaseInitial_isLongHeaderPaddedTo1200()
    {
        var pkt = InternetProbe.QuicGreaseInitial();
        Assert.Equal(1200, pkt.Length);
        Assert.Equal(0xc0, pkt[0]);
        Assert.Equal(0x0a, pkt[1]);
        Assert.Equal(0x0a, pkt[4]);
    }

    [Fact]
    public void ProbeLabel_formatsTcpTlsHttps()
    {
        Assert.Equal(
            "TCP 42 · TLS 12 · HTTPS 88",
            PublicEgressDiagnostics.ProbeLabel(new EgressHostProbe(42, 12, 88)));
        Assert.Equal(
            "TCP 0 · TLS 0 · HTTPS 0",
            PublicEgressDiagnostics.ProbeLabel(new EgressHostProbe()));
    }

    [Fact]
    public void FromProbes_and_FormatMs()
    {
        Assert.Equal(ServerReachStatus.Offline, ServerReachState.FromProbes(-1, -1).Status);
        var online = ServerReachState.FromProbes(10, 20);
        Assert.Equal(ServerReachStatus.Online, online.Status);
        Assert.Equal(10, online.LatencyMs);
        Assert.Equal(20, online.TlsMs);
        Assert.Equal("~10ms", ServerReachState.FormatMs(10));
        Assert.Equal("n/a", ServerReachState.FormatMs(null));
    }

    [Fact]
    public async Task Probe_unresolvableHost_returnsOffline()
    {
        var profile = ServerProfile.FromProxy(
            new Hysteria2Profile(
                "TEST-OFFLINE",
                "no-such-host.invalid",
                443,
                "p",
                "example.com"));
        var state = await ServerReachProbe.ProbeAsync(profile);
        Assert.Equal(ServerReachStatus.Offline, state.Status);
    }

    [Fact]
    public async Task Probe_hy2LiteralIp_fillsTlsFromTcpFallback()
    {
        // Listening TCP + no UDP reply: TCP RTT ok, QUIC fails → HY2 ping falls back to TCP.
        // (127.0.0.1:1 often blackholes on Windows instead of ConnectionRefused.)
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var profile = ServerProfile.FromProxy(
            new Hysteria2Profile("HY2-DNS", "127.0.0.1", port, "p", "example.com"));
        var state = await ServerReachProbe.ProbeAsync(profile);
        Assert.Equal(ServerReachStatus.Online, state.Status);
        Assert.NotNull(state.LatencyMs);
        Assert.True(state.LatencyMs >= 0);
        Assert.Equal(state.LatencyMs, state.TlsMs);
    }
}
