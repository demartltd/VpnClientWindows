using System.Diagnostics;
using System.Runtime.InteropServices;
using DemarVpn.Services;

namespace DemarVpn.Services.Vpn;

/// <summary>Elevated worker: runs sing-box and kills it when parent app exits.</summary>
public static class CoreWatchEntry
{
    private const uint ProcessSynchronize = 0x00100000;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0;

    public static string StartupLogPath => Path.Combine(AppLog.JournalDir, "sing-box-startup.log");

    public static bool TryRunIfRequested()
    {
        // runas/shell often leaves Main(args) empty; full command line is reliable.
        if (!CoreWatchArgs.TryParse(Environment.GetCommandLineArgs(), out var parentPid, out var exe, out var coreArgv))
            return false;

        Run(parentPid, exe, coreArgv);
        Environment.Exit(0);
        return true;
    }

    internal static void Run(int parentPid, string exe, IReadOnlyList<string> coreArgv)
    {
        AppLog.Init();
        AppLog.Append($"[vpn] core-watch begin parent={parentPid}");

        WintunCleanup.RemoveAll();

        var parent = OpenProcess(ProcessSynchronize, false, parentPid);
        if (parent == IntPtr.Zero)
        {
            AppLog.Append($"[vpn] core-watch open parent failed pid={parentPid}");
            Environment.Exit(1);
        }

        var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        using var core = StartCore(exe, coreArgv, workDir);
        if (core is null)
        {
            CloseHandle(parent);
            Environment.Exit(1);
        }

        AppLog.Append($"[vpn] core-watch sing-box pid={core.Id}");

        var handles = new[] { parent, core.Handle };
        var signaled = WaitForMultipleObjects(2, handles, false, Infinite);
        if (signaled == WaitObject0)
        {
            try { if (!core.HasExited) core.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
        }

        CloseHandle(parent);
    }

    private static Process? StartCore(string exe, IReadOnlyList<string> coreArgv, string workDir)
    {
        try
        {
            Directory.CreateDirectory(AppLog.JournalDir);
            File.WriteAllText(StartupLogPath, string.Empty);

            // Log goes to sing-box.log via config — no stderr redirect (avoids pipe backpressure stall).
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };
            foreach (var arg in coreArgv)
                psi.ArgumentList.Add(arg);
            psi.Environment["PATH"] = $"{workDir}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";

            var process = Process.Start(psi);
            if (process is null)
            {
                AppLog.Append("[vpn] core-watch start failed: Process.Start returned null");
                return null;
            }

            return process;
        }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] core-watch start failed: {ex.Message}");
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(
        uint count, IntPtr[] handles, bool waitAll, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
