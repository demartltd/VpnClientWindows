using DemarVpn.Models;
using DemarVpn.Services.Device;
using DemarVpn.Services.Network;

namespace DemarVpn.Tests;

public class ServiceMenuDiagnosticsTest
{
    [Fact]
    public void ClassifyRoute_connected_isDemarVpn()
    {
        var route = PublicEgressDiagnostics.ClassifyRoute(VpnState.Connected);
        Assert.Equal(EgressRouteKind.DemarVpn, route);
    }

    [Fact]
    public void ClassifyRoute_osVpnWithoutDemar_isOtherVpn()
    {
        var route = PublicEgressDiagnostics.ClassifyRoute(
            VpnState.Disconnected,
            new EgressRouteSnapshot(OsVpnActive: true, DemarConnected: false, DiagProxyReady: false));
        Assert.Equal(EgressRouteKind.OtherVpn, route);
    }

    [Fact]
    public void ClassifyRoute_disconnected_isDirect()
    {
        var route = PublicEgressDiagnostics.ClassifyRoute(VpnState.Disconnected);
        Assert.Equal(EgressRouteKind.Direct, route);
    }

    [Fact]
    public void CensorPublicIpv4_masksFirstOctets()
    {
        Assert.Equal("**.***.3.4", PublicEgressDiagnostics.CensorPublicIpv4("1.2.3.4"));
        Assert.Equal("localhost", PublicEgressDiagnostics.CensorPublicIpv4("127.0.0.1"));
    }

    [Fact]
    public void ParseCloudflareTrace_extractsIpAndCountry()
    {
        const string body = """
            fl=123
            ip=203.0.113.10
            loc=FI
            """;
        var info = PublicEgressDiagnostics.ParseCloudflareTrace(body);
        Assert.NotNull(info);
        Assert.Equal("203.0.113.10", info!.Ip);
        Assert.Equal("Finland", info.Country);
    }

    [Fact]
    public void CountryName_mapsIsoCodeToFullName()
    {
        Assert.Equal("Finland", PublicEgressDiagnostics.CountryName("FI"));
    }

    [Fact]
    public void DeviceFingerprint_isDeterministic()
    {
        var fields = new Dictionary<string, string>
        {
            ["windows_machine_guid"] = "abc-123",
            ["windows_computer_name"] = "PC-TEST",
        };
        var a = DeviceIdentity.Fingerprint(fields);
        var b = DeviceIdentity.Fingerprint(fields);
        Assert.Equal(a, b);
        Assert.Equal(32, a.Length);
    }

    [Fact]
    public void ProbeLabel_formatsTcpAndHttps()
    {
        Assert.Equal("TCP 42 · HTTPS 88", PublicEgressDiagnostics.ProbeLabel(new EgressHostProbe(42, 88)));
    }
}
