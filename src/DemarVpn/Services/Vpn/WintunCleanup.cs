using System.Diagnostics;

namespace DemarVpn.Services.Vpn;

internal static class WintunCleanup
{
    public static void RemoveAll()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "interface delete interface name=\"DemarVPN\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(1_000);
        }
        catch
        {
            /* best effort */
        }
    }
}
