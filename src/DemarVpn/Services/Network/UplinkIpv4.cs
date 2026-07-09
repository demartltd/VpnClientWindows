using System.Net;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class UplinkIpv4
{
    public static Task<IReadOnlyList<string>> ResolveAsync(string host) => ResolveAsync(host, 2);

    public static async Task<IReadOnlyList<string>> ResolveAsync(string host, int attempts)
    {
        var trimmed = host.Trim();
        if (trimmed.Length == 0) return [];
        if (HostUtils.IsLiteralIpv4(trimmed)) return [trimmed];

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var result = await ResolveOnceAsync(trimmed);
            if (result.Count > 0) return result;
            if (attempt < attempts - 1) await Task.Delay(200);
        }
        return [];
    }

    private static async Task<IReadOnlyList<string>> ResolveOnceAsync(string host)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host);
            return addrs
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Where(s => s.Length > 0)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
