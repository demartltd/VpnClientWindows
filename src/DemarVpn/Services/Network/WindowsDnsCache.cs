using System.Runtime.InteropServices;

namespace DemarVpn.Services.Network;

public static class WindowsDnsCache
{
    public static void Flush()
    {
        try { DnsFlushResolverCache(); }
        catch { /* ignore */ }
    }

    [DllImport("dnsapi.dll")]
    private static extern uint DnsFlushResolverCache();
}
