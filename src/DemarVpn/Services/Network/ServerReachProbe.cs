using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class ServerReachProbe
{
    /// <summary>
    /// TCP + TLS; HY2 uses QUIC VN (salamander-wrapped when obfs is set). TCP RTT fallback for HY2.
    /// </summary>
    public static async Task<ServerReachState> ProbeAsync(
        ServerProfile profile, CancellationToken ct = default)
    {
        var host = profile.ConnectPlan.UplinkHost.Trim();
        if (host.Length == 0) return ServerReachState.Offline;

        var port = profile.UplinkPort;
        var hy2 = profile.Protocol == ServerProtocol.Hysteria2;
        string? salamander = hy2 && profile.Profile is Hysteria2Profile { ObfsPassword: { Length: > 0 } pw }
            ? pw
            : null;
        // Plain HY2: ignore TCP RST (~1–2ms noise). Salamander: keep TCP as fallback.
        var countRefused = !hy2 || salamander is not null;
        var tcpTask = InternetProbe.ProbeTcpMsAsync(host, port, ct, countRefused: countRefused);
        var hsTask = hy2
            ? InternetProbe.ProbeQuicMsAsync(host, port, ct, salamander)
            : InternetProbe.ProbeTlsMsAsync(host, port, ct);
        await Task.WhenAll(tcpTask, hsTask).ConfigureAwait(false);
        var tcpMs = await tcpTask.ConfigureAwait(false);
        var hsMs = await hsTask.ConfigureAwait(false);
        var pingMs = hsMs >= 0 ? hsMs : hy2 && tcpMs >= 0 ? tcpMs : -1;

        AppLog.Append(
            $"InternetProbe {host}:{port} tcp={Ms(tcpMs)} {(hy2 ? "quic" : "tls")}={Ms(hsMs)} ping={Ms(pingMs)}");

        return ServerReachState.FromProbes(tcpMs, pingMs);
    }

    private static string Ms(int v) => v >= 0 ? $"{v}ms" : "fail";
}
