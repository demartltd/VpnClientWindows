using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class ServerReachProbe
{
    /// <summary>
    /// TCP + TLS; HY2 uses QUIC VN for the handshake slot (parity Android UplinkProbe).
    /// </summary>
    public static async Task<ServerReachState> ProbeAsync(
        ServerProfile profile, CancellationToken ct = default)
    {
        var host = profile.ConnectPlan.UplinkHost.Trim();
        if (host.Length == 0) return ServerReachState.Offline;

        var port = profile.UplinkPort;
        var hy2 = profile.Protocol == ServerProtocol.Hysteria2;
        var tcpTask = InternetProbe.ProbeTcpMsAsync(host, port, ct);
        var hsTask = hy2
            ? InternetProbe.ProbeQuicMsAsync(host, port, ct)
            : InternetProbe.ProbeTlsMsAsync(host, port, ct);
        await Task.WhenAll(tcpTask, hsTask).ConfigureAwait(false);
        var tcpMs = await tcpTask.ConfigureAwait(false);
        var hsMs = await hsTask.ConfigureAwait(false);
        // HY2: if UDP filtered, still surface TCP RTT in the list metric.
        var pingMs = hsMs >= 0 ? hsMs : hy2 && tcpMs >= 0 ? tcpMs : hsMs;

        var tcpText = tcpMs >= 0 ? $"{tcpMs}ms" : "fail";
        var hsText = hsMs >= 0 ? $"{hsMs}ms" : "fail";
        var pingText = pingMs >= 0 ? $"{pingMs}ms" : "fail";
        AppLog.Append(
            $"InternetProbe {host}:{port} tcp={tcpText} {(hy2 ? "quic" : "tls")}={hsText} ping={pingText}");

        return ServerReachState.FromProbes(tcpMs, pingMs);
    }
}
