using System.Diagnostics;

namespace DemarVpn.Services.Vpn;

internal static class WintunCleanup
{
    public static void RemoveAll()
    {
        try
        {
            using var ps = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -NonInteractive -Command " +
                    "\"Get-NetAdapter | Where-Object { $_.InterfaceDescription -like '*Wintun*' } " +
                    "| Remove-NetAdapter -Confirm:$false -ErrorAction SilentlyContinue\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            ps?.WaitForExit(8_000);
        }
        catch
        {
            /* best effort */
        }
    }
}
