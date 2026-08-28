using System.Diagnostics;

namespace DemarVpn.Services.Vpn;

internal static class CoreProcess
{
    public static bool AnyRunning()
    {
        foreach (var proc in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                return true;
            }
            finally
            {
                proc.Dispose();
            }
        }

        return false;
    }

    public static int? FirstPid()
    {
        foreach (var proc in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                return proc.Id;
            }
            finally
            {
                proc.Dispose();
            }
        }

        return null;
    }

    public static bool IsRunning(int pid)
    {
        foreach (var proc in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                if (proc.Id == pid) return true;
            }
            finally
            {
                proc.Dispose();
            }
        }

        return false;
    }

    public static void KillAll()
    {
        foreach (var proc in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
            finally
            {
                proc.Dispose();
            }
        }
    }
}
