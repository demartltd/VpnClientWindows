using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace DemarVpn.Services;

/// <summary>Logon scheduled task autostart — default ON.</summary>
public static class RunAtStartup
{
    const string TaskName = Strings.AppName;

    static readonly string PrefsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "run_at_startup");

    static int _enabled = 1;

    public static bool Enabled => Volatile.Read(ref _enabled) != 0;

    public static void Init()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return;
            Volatile.Write(ref _enabled, PrefMeansEnabled(File.ReadAllText(PrefsPath)) ? 1 : 0);
        }
        catch { /* ignore */ }
    }

    public static void Set(bool enabled)
    {
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            File.WriteAllText(PrefsPath, enabled ? "1" : "0");
        }
        catch { /* ignore */ }
        Sync();
    }

    public static void Sync()
    {
        try
        {
            if (Enabled) EnsureRegistered();
            else Unregister();
        }
        catch (Exception ex)
        {
            AppLog.Append($"run-at-startup sync failed: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        DeleteTask();
    }

    internal static bool PrefMeansEnabled(string? text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim() is not ("0" or "false");

    static void EnsureRegistered()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        exe = Path.GetFullPath(exe);
        if (!TryEnsureTask(exe))
            AppLog.Append("run-at-startup task registration did not complete");
    }

    static bool TryEnsureTask(string exe)
    {
        if (IsTaskCurrent(exe)) return true;
        try
        {
            var user = WindowsIdentity.GetCurrent().Name;
            var workDir = Path.GetDirectoryName(exe)!;
            var xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo><Description>Demar VPN autostart</Description></RegistrationInfo>
                  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Esc(user)}</UserId></LogonTrigger></Triggers>
                  <Principals><Principal id="Author"><UserId>{Esc(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
                  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><Enabled>true</Enabled></Settings>
                  <Actions Context="Author"><Exec><Command>{Esc(exe)}</Command><WorkingDirectory>{Esc(workDir)}</WorkingDirectory></Exec></Actions>
                </Task>
                """;
            var path = Path.Combine(Path.GetTempPath(), "demar-autostart.xml");
            File.WriteAllText(path, xml, Encoding.Unicode);
            var ok = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{path}\" /F") == 0;
            TryDelete(path);
            if (!ok)
            {
                AppLog.Append("run-at-startup task register failed: schtasks exit");
                return false;
            }
            return IsTaskCurrent(exe);
        }
        catch (Exception ex)
        {
            AppLog.Append($"run-at-startup task register failed: {ex.Message}");
            return false;
        }
    }

    static bool IsTaskCurrent(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\" /FO LIST /V")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null || p.WaitForExit(5000) != true || p.ExitCode != 0) return false;
            foreach (var line in p.StandardOutput.ReadToEnd().Split('\n'))
            {
                if (!line.Contains("Task To Run:", StringComparison.OrdinalIgnoreCase)) continue;
                var cmd = line.Split(':', 2)[1].Trim().Trim('"');
                return PathsEqual(cmd, exe);
            }
        }
        catch { /* ignore */ }
        return false;
    }

    static void DeleteTask() => Schtasks($"/Delete /TN \"{TaskName}\" /F");

    static int Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        p?.WaitForExit(5000);
        return p?.ExitCode ?? 1;
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }

    static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
