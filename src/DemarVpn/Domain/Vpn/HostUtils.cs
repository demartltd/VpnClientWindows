namespace DemarVpn.Domain.Vpn;

public static class HostUtils
{
    public static bool IsLiteralIpv4(string host)
    {
        var parts = host.Split('.');
        if (parts.Length != 4) return false;
        return parts.All(p => int.TryParse(p, out var n) && n is >= 0 and <= 255);
    }

    public static bool IsHostname(string host)
    {
        var h = host.Trim();
        return h.Length > 0 && !IsLiteralIpv4(h);
    }
}
