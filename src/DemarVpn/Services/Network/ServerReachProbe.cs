using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class ServerReachProbe
{
    /// <summary>Hysteria2 is QUIC/UDP — DNS resolve success counts as Online.</summary>
    public static async Task<ServerReachState> ProbeAsync(ServerProfile profile)
    {
        var host = profile.ConnectPlan.UplinkHost;
        var target = (await UplinkIpv4.ResolveAsync(host)).FirstOrDefault()
                     ?? (HostUtils.IsLiteralIpv4(host) ? host : null);
        if (target is null)
            return new ServerReachState(ServerReachStatus.Offline);
        return new ServerReachState(ServerReachStatus.Online);
    }
}
