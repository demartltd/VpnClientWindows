using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class ServerReachProbe
{
    /// <summary>Hysteria2 is QUIC/UDP — DNS resolve success counts as Online.</summary>
    public static async Task<ServerReachState> ProbeAsync(ServerProfile profile)
    {
        var host = profile.ConnectPlan.UplinkHost;
        var port = profile.UplinkPort;
        var target = (await UplinkIpv4.ResolveAsync(host)).FirstOrDefault()
                     ?? (HostUtils.IsLiteralIpv4(host) ? host : null);
        if (target is null)
        {
            AppLog.Append($"UplinkProbe {profile.DisplayName} {host}:{port}=fail (dns)");
            return new ServerReachState(ServerReachStatus.Offline);
        }
        AppLog.Append($"UplinkProbe {profile.DisplayName} {target}:{port}=dns-ok");
        return new ServerReachState(ServerReachStatus.Online);
    }
}
