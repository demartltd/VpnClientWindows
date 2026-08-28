using DemarVpn.Services.Vpn;
using Microsoft.UI.Xaml;

namespace DemarVpn;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (CoreStopEntry.TryRunIfRequested())
            return;
        if (CoreWatchEntry.TryRunIfRequested())
            return;

        Application.Start(_ => new App());
    }
}
