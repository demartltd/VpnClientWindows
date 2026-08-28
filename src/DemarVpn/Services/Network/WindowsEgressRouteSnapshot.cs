using System.Net.NetworkInformation;
using DemarVpn.Models;

namespace DemarVpn.Services.Network;

public sealed record EgressRouteSnapshot(bool OsVpnActive, bool DemarConnected);

public static class WindowsEgressRouteSnapshot
{
    public static Task<EgressRouteSnapshot> CaptureAsync(VpnState state)
    {
        var demar = state.Kind == VpnStateKind.Connected;
        var osVpn = !demar && NetworkInterface.GetAllNetworkInterfaces().Any(LooksLikeVpn);
        return Task.FromResult(new EgressRouteSnapshot(osVpn, demar));
    }

    private static bool LooksLikeVpn(NetworkInterface n)
    {
        if (n.OperationalStatus != OperationalStatus.Up) return false;
        if (n.NetworkInterfaceType is NetworkInterfaceType.Ppp) return true;
        var text = $"{n.Name} {n.Description}".ToUpperInvariant();
        return text.Contains("VPN") || text.Contains("WIREGUARD") || text.Contains("WINTUN")
            || text.Contains("TAP") || text.Contains("TUNNEL");
    }
}
