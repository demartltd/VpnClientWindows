using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Vpn;

public enum TunnelReadyResult { Ok, TcpFail, ProxyFail, TunFail }

public static class VpnTunnelProbe
{
    private const int RetryDelayMs = 400;
    private const int ReadyPollMs = 150;
    private const int TcpProbeMs = 1_200;
    private const int ProxyOkRequired = 2;
    private const int DiagPortBudgetMs = 2_000;

    public static async Task<TunnelReadyResult> WaitForTunnelReadyAsync(int budgetMs, CancellationToken cancellationToken = default)
    {
        if (budgetMs <= 0) return TunnelReadyResult.TcpFail;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);

        if (!await WaitForDiagPortAsync(Math.Min(DiagPortBudgetMs, budgetMs), cancellationToken))
            return TunnelReadyResult.TcpFail;

        await Task.Delay(ConnectTimeouts.ProxyWarmupMs, cancellationToken);

        var proxyOk = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TunnelPathProbe.TraceOkAsync(TunnelPathProbe.DiagClient, TunnelPathProbe.TraceIpUrl, cancellationToken))
            {
                if (++proxyOk >= ProxyOkRequired) break;
            }
            else
            {
                proxyOk = 0;
            }

            if (!await DelayRemainingAsync(deadline, cancellationToken))
                return TunnelReadyResult.ProxyFail;
        }

        if (proxyOk < ProxyOkRequired) return TunnelReadyResult.ProxyFail;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TunnelPathProbe.TraceOkAsync(TunnelPathProbe.TunClient, TunnelPathProbe.TraceHostUrl, cancellationToken))
                return TunnelReadyResult.Ok;
            if (!await DelayRemainingAsync(deadline, cancellationToken))
                return TunnelReadyResult.TunFail;
        }

        return TunnelReadyResult.TunFail;
    }

    private static async Task<bool> WaitForDiagPortAsync(int budgetMs, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TunnelPathProbe.TcpDiagOpenAsync(TcpProbeMs, cancellationToken))
                return true;
            await Task.Delay(ReadyPollMs, cancellationToken);
        }
        return false;
    }

    private static async Task<bool> DelayRemainingAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var remaining = (deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
        if (remaining <= 0) return false;
        await Task.Delay((int)Math.Min(RetryDelayMs, remaining), cancellationToken);
        return true;
    }
}
