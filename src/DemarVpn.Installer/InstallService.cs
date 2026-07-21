using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using IOPath = System.IO.Path;
#if EMBED_PAYLOAD
using System.IO.Compression;
#endif

namespace DemarVpn.Installer;

public static class InstallService
{
    public const string AppName = "Demar VPN";
    public const string AppExeName = "Demar VPN.exe";
    public const string Publisher = "Remader";
    const string ArpKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Demar VPN";

    static string MarkerPath =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "install_location");

    public static string DefaultInstallDir =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public static string StartMenuLnk =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs", $"{AppName}.lnk");

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    /// <summary>Target for install/update: AppData marker → ARP → default.</summary>
    public static string ResolveTargetInstallDir()
    {
        if (TryReadMarker() is { } marker) return marker;
        if (TryReadArpInstallDir() is { } arp) { WriteMarker(arp); return arp; }
        return DefaultInstallDir;
    }

    /// <summary>Uninstall.exe folder → AppData → ARP → default.</summary>
    public static string ResolveInstallDirFromArp()
    {
        var self = Environment.ProcessPath;
        if (self is not null
            && IOPath.GetFileNameWithoutExtension(self).Equals("Uninstall", StringComparison.OrdinalIgnoreCase)
            && IOPath.GetDirectoryName(self) is { Length: > 0 } dir)
            return IOPath.GetFullPath(dir);
        return TryReadMarker() ?? TryReadArpInstallDir() ?? DefaultInstallDir;
    }

    static string? TryReadMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            var line = File.ReadAllText(MarkerPath).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(line)) return null;
            return IOPath.GetFullPath(line.TrimEnd('\\', '/'));
        }
        catch { return null; }
    }

    static string? TryReadArpInstallDir()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ArpKey);
            var loc = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(loc)) return null;
            return IOPath.GetFullPath(loc.Trim().TrimEnd('\\', '/'));
        }
        catch { return null; }
    }

    static void WriteMarker(string installDir)
    {
        try
        {
            var dir = IOPath.GetDirectoryName(MarkerPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(MarkerPath, IOPath.GetFullPath(installDir));
        }
        catch { /* ignore */ }
    }

    static void ClearMarker()
    {
        try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); }
        catch { /* ignore */ }
    }

#if EMBED_PAYLOAD
    public static void Install(string installDir, IProgress<string>? progress)
    {
        installDir = IOPath.GetFullPath(installDir.Trim());
        if (string.IsNullOrWhiteSpace(installDir))
            throw new InvalidOperationException("Путь установки пуст");

        var previous = TryReadMarker() ?? TryReadArpInstallDir();

        progress?.Report("Остановка процессов…");
        StopVpnAndWait(installDir, previous);

        Directory.CreateDirectory(installDir);

        progress?.Report("Очистка старых файлов…");
        ClearInstallDir(installDir);

        progress?.Report("Распаковка…");
        ExtractPayloadWithRetry(installDir);

        progress?.Report("Регистрация…");
        var uninstallExe = IOPath.Combine(installDir, "Uninstall.exe");
        ExtractEmbeddedUninstall(uninstallExe);

        var appExe = IOPath.Combine(installDir, AppExeName);
        if (!File.Exists(appExe))
            throw new InvalidOperationException($"{AppExeName} missing after extract");

        CreateShortcut(StartMenuLnk, appExe, installDir);
        WriteArp(installDir, appExe, uninstallExe);
        WriteMarker(installDir);
        progress?.Report("Готово");
    }

    static void ClearInstallDir(string installDir)
    {
        if (!Directory.Exists(installDir)) return;
        foreach (var path in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories))
            ForceDeleteFile(path);
        foreach (var sub in Directory.EnumerateDirectories(installDir).OrderByDescending(d => d.Length))
            TryDeleteDir(sub);
    }

    static void ExtractPayloadWithRetry(string installDir)
    {
        const int attempts = 5;
        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                using var zip = OpenPayload();
                zip.ExtractToDirectory(installDir, overwriteFiles: true);
                return;
            }
            catch (IOException) when (i < attempts)
            {
                StopVpnAndWait(installDir, null);
                Thread.Sleep(500 * i);
            }
        }
        using (var zip = OpenPayload())
            zip.ExtractToDirectory(installDir, overwriteFiles: true);
    }

    static void ExtractEmbeddedUninstall(string destPath)
    {
        ForceDeleteFile(destPath);
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("uninstall.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("uninstall.exe not embedded — rebuild via build_windows.ps1");
        using var src = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Cannot open uninstall.exe");
        using var dst = File.Create(destPath);
        src.CopyTo(dst);
    }

    static ZipArchive OpenPayload()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("payload.zip not embedded — rebuild via build_windows.ps1");
        var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Cannot open payload.zip");
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    static void WriteArp(string installDir, string appExe, string uninstallExe)
    {
        using var key = Registry.LocalMachine.CreateSubKey(ArpKey)
            ?? throw new InvalidOperationException("Cannot write ARP");
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", AppVersion);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", appExe);
        key.SetValue("UninstallString", $"\"{uninstallExe}\"");
        key.SetValue("QuietUninstallString", $"\"{uninstallExe}\" /silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    static void CreateShortcut(string lnkPath, string target, string workDir)
    {
        Directory.CreateDirectory(IOPath.GetDirectoryName(lnkPath)!);
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(type)!;
        var s = shell.CreateShortcut(lnkPath);
        s.TargetPath = target;
        s.WorkingDirectory = workDir;
        s.Description = AppName;
        s.Save();
        if (!File.Exists(lnkPath))
            throw new InvalidOperationException("Start Menu shortcut failed");
    }
#endif

    public static void Uninstall(IProgress<string>? progress)
    {
        var dir = ResolveInstallDirFromArp();

        progress?.Report("Остановка процессов…");
        StopVpnAndWait(dir, null);

        progress?.Report("Удаление…");
        TryDelete(StartMenuLnk);
        DeleteArp();
        ClearMarker();

        var self = Environment.ProcessPath;
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (self is not null && PathsEqual(path, self)) continue;
                ForceDeleteFile(path);
            }
            foreach (var sub in Directory.EnumerateDirectories(dir).OrderByDescending(d => d.Length))
                TryDeleteDir(sub);
        }

        progress?.Report("Завершение…");
        ScheduleSelfDelete(self, dir);
    }

    /// <summary>Kill Demar VPN / sing-box and any process under install roots; wait until gone.</summary>
    static void StopVpnAndWait(string? primaryRoot, string? secondaryRoot)
    {
        var roots = new List<string>();
        void AddRoot(string? r)
        {
            if (string.IsNullOrWhiteSpace(r)) return;
            var full = IOPath.GetFullPath(r);
            if (!roots.Any(x => PathsEqual(x, full))) roots.Add(full);
        }
        AddRoot(primaryRoot);
        AddRoot(secondaryRoot);

        var self = Environment.ProcessPath;
        var deadline = DateTime.UtcNow.AddSeconds(12);
        do
        {
            foreach (var name in new[] { "Demar VPN", "sing-box" })
                foreach (var p in Process.GetProcessesByName(name))
                    TryKill(p, self);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var path = SafePath(p);
                    if (path is null || !roots.Any(r => UnderRoot(path, r))) continue;
                    TryKill(p, self);
                    continue; // TryKill disposed
                }
                catch { /* ignore */ }
                p.Dispose();
            }

            if (!AnyVpnAlive(roots, self)) break;
            Thread.Sleep(400);
        } while (DateTime.UtcNow < deadline);
        Thread.Sleep(300);
    }

    static bool AnyVpnAlive(List<string> roots, string? self)
    {
        foreach (var name in new[] { "Demar VPN", "sing-box" })
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (!IsSelf(SafePath(p), self)) return true;
                }
                catch { return true; }
                finally { p.Dispose(); }
            }

        foreach (var root in roots)
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == Environment.ProcessId) continue;
                    var path = SafePath(p);
                    if (path is null || IsSelf(path, self)) continue;
                    if (UnderRoot(path, root)) return true;
                }
                catch { /* ignore */ }
                finally { p.Dispose(); }
            }
        return false;
    }

    static void TryKill(Process p, string? self)
    {
        try
        {
            if (p.Id == Environment.ProcessId || IsSelf(SafePath(p), self)) return;
            p.Kill(entireProcessTree: true);
            p.WaitForExit(4000);
        }
        catch { /* ignore */ }
        finally { p.Dispose(); }
    }

    static bool IsSelf(string? path, string? self) =>
        self is not null && path is not null && PathsEqual(path, self);

    static bool UnderRoot(string? path, string root) =>
        path is not null
        && path.StartsWith(root.TrimEnd('\\', '/') + IOPath.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    static string? SafePath(Process p)
    {
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            var handle = OpenProcess(0x1000 /* QUERY_LIMITED */, false, p.Id);
            if (handle == 0) return null;
            try
            {
                if (!QueryFullProcessImageNameW(handle, 0, sb, ref size)) return null;
                return sb.ToString();
            }
            finally { CloseHandle(handle); }
        }
        catch { return null; }
    }

    static void DeleteArp() =>
        Registry.LocalMachine.DeleteSubKey(ArpKey, throwOnMissingSubKey: false);

    static void ScheduleSelfDelete(string? self, string appDir)
    {
        // Retries: locked Uninstall.exe / AV; always target the resolved install dir (custom paths too).
        var bat = IOPath.Combine(IOPath.GetTempPath(), $"demar-uninst-{Guid.NewGuid():N}.cmd");
        var selfArg = self is null ? "" : $"del /f /q \"{self}\" >nul 2>&1";
        File.WriteAllText(bat, $"""
            @echo off
            timeout /t 2 /nobreak >nul
            {selfArg}
            for /L %%i in (1,1,8) do (
              rd /s /q "{appDir}" >nul 2>&1
              if not exist "{appDir}" goto done
              timeout /t 1 /nobreak >nul
            )
            rd /s /q "{appDir}" >nul 2>&1
            :done
            del /f /q "%~f0" >nul 2>&1
            """);
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = IOPath.GetTempPath(),
        });
    }

    static void ForceDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch
        {
            try
            {
                Thread.Sleep(200);
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch { /* ignore */ }
        }
    }

    static bool PathsEqual(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(IOPath.GetFullPath(a), IOPath.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }

    static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* ignore */ }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern nint OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageNameW(nint hProcess, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(nint handle);
}
