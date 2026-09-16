using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class UplinkIpv4
{
    private const int AttemptMs = 3_000;
    private static readonly ConcurrentDictionary<string, string[]> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<IReadOnlyList<string>> ResolveAsync(string host, CancellationToken ct = default) =>
        ResolveAsync(host, 2, ct);

    public static async Task<(IReadOnlyList<string> Ips, string? Error)> ResolveDetailedAsync(
        string host, int attempts = 2, CancellationToken ct = default)
    {
        var trimmed = host.Trim();
        if (trimmed.Length == 0) return ([], "empty host");
        if (HostUtils.IsLiteralIpv4(trimmed)) return ([trimmed], null);

        // Warm cache (reachability / prior connect) — skip flaky DNS wait.
        if (Cache.TryGetValue(trimmed, out var cached) && cached.Length > 0)
            return (cached, null);

        string? last = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(AttemptMs);
                var ips = (await Dns.GetHostAddressesAsync(trimmed, cts.Token))
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .Where(s => s.Length > 0)
                    .ToArray();
                if (ips.Length > 0)
                {
                    Cache[trimmed] = ips;
                    return (ips, null);
                }
                last = "no A records";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex is OperationCanceledException ? $"timeout {AttemptMs}ms" : ex.Message;
            }
            if (attempt < attempts - 1) await Task.Delay(100, ct);
        }

        if (Cache.TryGetValue(trimmed, out cached) && cached.Length > 0)
            return (cached, null);

        return ([], last ?? "resolve failed");
    }

    public static async Task<IReadOnlyList<string>> ResolveAsync(
        string host, int attempts, CancellationToken ct = default)
    {
        var (ips, _) = await ResolveDetailedAsync(host, attempts, ct);
        return ips;
    }
}
