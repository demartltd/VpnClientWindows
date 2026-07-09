using System.Net.Sockets;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class UplinkProbe
{
    public const int ConnectPreflightTimeoutMs = ConnectTimeouts.PreflightMs;

    public static async Task<bool> TcpReachableAsync(
        string host, int port, int timeoutMs = ConnectPreflightTimeoutMs, CancellationToken cancellationToken = default)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            await socket.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string?> FirstReachableAsync(
        SingBoxConnectPlan plan, IReadOnlyList<string> resolved, int port,
        CancellationToken cancellationToken = default, int timeoutMs = ConnectPreflightTimeoutMs)
    {
        foreach (var target in Targets(plan, resolved))
        {
            if (await TcpReachableAsync(target, port, timeoutMs, cancellationToken))
                return target;
        }
        return null;
    }

    public static string? ConnectTarget(SingBoxConnectPlan plan, IReadOnlyList<string> resolved) =>
        Targets(plan, resolved).FirstOrDefault();

    private static IEnumerable<string> Targets(SingBoxConnectPlan plan, IReadOnlyList<string> resolved)
    {
        foreach (var ip in resolved) yield return ip;
        if (resolved.Count == 0 && HostUtils.IsLiteralIpv4(plan.UplinkHost))
            yield return plan.UplinkHost;
    }
}
