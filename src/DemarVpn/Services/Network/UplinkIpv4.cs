using System.Net;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class UplinkIpv4
{
    public static async Task<IReadOnlyList<string>> ResolveAsync(string host)
    {
        var trimmed = host.Trim();
        if (trimmed.Length == 0) return [];
        if (HostUtils.IsLiteralIpv4(trimmed)) return [trimmed];
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(trimmed);
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
