using Microsoft.UI.Xaml;
using DemarVpn.Services;

namespace DemarVpn.Services.Vpn;

public static class VpnLifecycle
{
    private static int _stopped;

    public static void Attach(Window window)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopOnce();
        window.Closed += (_, _) => StopOnce();
    }

    static void StopOnce()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        AppServices.Vpn.EnsureStoppedSync();
    }
}
