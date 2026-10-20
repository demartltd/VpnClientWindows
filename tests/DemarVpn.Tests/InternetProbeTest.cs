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

    [Fact]
    public async Task Probe_hy2PlainTcpRefused_isNotLatency()
    {
        // Plain HY2 (no salamander): closed TCP must not become ~1ms when QUIC fails.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var profile = ServerProfile.FromProxy(
            new Hysteria2Profile("HY2-RST", "127.0.0.1", port, "p", "example.com"));
        var state = await ServerReachProbe.ProbeAsync(profile);
        Assert.Equal(ServerReachStatus.Offline, state.Status);
        Assert.Null(state.TlsMs);
    }

    [Fact]
    public async Task Probe_hy2Salamander_tcpFallback_whenQuicSilent()
    {
        // No UDP reply on loopback: salamander-wrapped QUIC fails → TCP RTT is the list metric.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var profile = ServerProfile.FromProxy(
            new Hysteria2Profile("HY2-OBFS", "127.0.0.1", port, "p", "example.com", ObfsPassword: "secret"));
        var state = await ServerReachProbe.ProbeAsync(profile);
        Assert.Equal(ServerReachStatus.Online, state.Status);
        Assert.NotNull(state.TlsMs);
        Assert.Equal(state.LatencyMs, state.TlsMs);
    }

    [Fact]
    public void SalamanderObfs_blake2b256_matchesAppleVectors()
    {
        Assert.Equal(
            "0e5751c026e543b2e8ab2eb06099daa1d1e5df47778f7787faab45cdf12fe3a8",
            Convert.ToHexString(SalamanderObfs.Blake2b256([])).ToLowerInvariant());
        Span<byte> msg = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount("obfs-pass") + 8];
        var n = System.Text.Encoding.UTF8.GetBytes("obfs-pass", msg);
        ReadOnlySpan<byte> salt = [1, 2, 3, 4, 5, 6, 7, 8];
        salt.CopyTo(msg[n..]);
        Assert.Equal(
            "df660a36d9734cd21f02e6db3ce7d3a24f2b18597d12dfe116cb8e5d70807b0a",
            Convert.ToHexString(SalamanderObfs.Blake2b256(msg[..(n + 8)])).ToLowerInvariant());
    }

    [Fact]
    public void SalamanderObfs_wrap_addsSaltPrefix()
    {
        var payload = new byte[1200];
        Array.Fill(payload, (byte)0xAB);
        var wrapped = SalamanderObfs.Wrap(payload, "obfs-pass");
        Assert.Equal(1200 + SalamanderObfs.SaltLen, wrapped.Length);
        Assert.False(wrapped.AsSpan(SalamanderObfs.SaltLen).SequenceEqual(payload));
    }
}
