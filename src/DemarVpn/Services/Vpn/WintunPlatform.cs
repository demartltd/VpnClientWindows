#if HAS_LIBBOX
using System.Runtime.InteropServices;
using SagerNet;

namespace DemarVpn.Services.Vpn;

public sealed class WintunPlatform : PlatformInterface
{
    public int TunFd { get; private set; } = -1;

    public void CloseTun() => TunFd = -1;

    public bool UsePlatformAutoDetectInterfaceControl() => true;

    public void AutoDetectInterfaceControl(int fd)
    {
        // sing-box binds outbound sockets; no Android VpnService.protect equivalent required on Windows.
    }

    public int OpenTun(TunOptions options)
    {
        // Libbox opens Wintun via sing-tun on Windows when stack=system; platform hook optional.
        TunFd = 1;
        AppLog.Append("[vpn] openTun delegated to libbox/system stack");
        return TunFd;
    }

    public bool UseProcFS() => false;
    public NetworkInterfaceIterator? GetInterfaces() => null;
    public void StartDefaultInterfaceMonitor(InterfaceUpdateListener listener) { }
    public void CloseDefaultInterfaceMonitor() { }
    public int FindConnectionOwner(int ipProtocol, string sourceAddress, int sourcePort,
        string destinationAddress, int destinationPort) => -1;
    public LocalDNSTransport? LocalDNSTransport() => null;
    public bool UnderNetworkExtension() => false;
    public bool IncludeAllNetworks() => false;
    public void WriteLog(string message) => AppLog.Append($"[libbox] {message}");
    public void SendNotification(Notification notification) { }
    public StringIterator? SystemCertificates() => null;
}
#endif
