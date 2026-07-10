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
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var ips = attempt == 0 ? resolved : await UplinkIpv4.ResolveAsync(plan.UplinkHost);
            var target = await RaceTargetsAsync(plan, ips, port, cancellationToken);
            if (target is not null) return target;
            if (attempt == 0 && !cancellationToken.IsCancellationRequested)
                await Task.Delay(200, cancellationToken);
        }
        return null;
    }

    public static async Task WaitForDiagPortClosedAsync(int budgetMs = ConnectTimeouts.DiagPortCloseMs)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await TcpReachableAsync(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, 200))
                return;
            await Task.Delay(100);
        }
    }

    public static string? ConnectTarget(SingBoxConnectPlan plan, IReadOnlyList<string> resolved) =>
        Targets(plan, resolved).FirstOrDefault();

    private static async Task<string?> RaceTargetsAsync(
        SingBoxConnectPlan plan, IReadOnlyList<string> resolved, int port, CancellationToken cancellationToken)
    {
        var targets = Targets(plan, resolved).ToList();
        if (targets.Count == 0) return null;

        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        raceCts.CancelAfter(ConnectTimeouts.PreflightRaceMs);

        var tasks = targets.Select(t => ProbeTargetAsync(t, port, raceCts.Token)).ToArray();
        while (tasks.Length > 0)
        {
            var done = await Task.WhenAny(tasks);
            if (await done is { } target) return target;
            tasks = tasks.Where(t => t != done).ToArray();
        }
        return null;
    }

    private static async Task<string?> ProbeTargetAsync(string target, int port, CancellationToken cancellationToken) =>
        await TcpReachableAsync(target, port, ConnectPreflightTimeoutMs, cancellationToken) ? target : null;

    private static IEnumerable<string> Targets(SingBoxConnectPlan plan, IReadOnlyList<string> resolved)
    {
        foreach (var ip in resolved) yield return ip;
        if (HostUtils.IsHostname(plan.UplinkHost)) yield return plan.UplinkHost;
        if (resolved.Count == 0 && HostUtils.IsLiteralIpv4(plan.UplinkHost))
            yield return plan.UplinkHost;
    }
}
