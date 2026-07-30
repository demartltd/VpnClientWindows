using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Network;

namespace DemarVpn.Services.Vpn;

public sealed class SingBoxProcessEngine : IVpnEngine
{
    private const int StartupPollMs = 100;
    private const int UacCancelledHResult = unchecked((int)0x800704C7);

    private Process? _process;
    private int? _ownedPid;
    private readonly StringBuilder _stderr = new();
    private readonly string _configPath;
    private readonly string _logPath;

    public SingBoxProcessEngine()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "demar");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "config.json");
        _logPath = Path.Combine(dir, "sing-box.log");
    }

    public bool IsRunning => TryGetProcess() is { HasExited: false };

    public async Task StartAsync(string configJson, CancellationToken cancellationToken)
    {
        await StopAsync();
        _stderr.Clear();
        if (File.Exists(_logPath)) File.Delete(_logPath);

        configJson = InjectLogOutput(configJson, _logPath);
        await File.WriteAllTextAsync(_configPath, configJson, cancellationToken);

        var exe = FindSingBoxExe();
        if (exe is null)
            throw new InvalidOperationException(
                "sing-box.exe not found. Run: py -3 scripts/for_builds/singbox-build-windows.py");

        var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        var args = $"run -c \"{_configPath}\"";
        _process = IsAdmin()
            ? StartDirect(exe, args, workDir)
            : StartElevated(exe, args, workDir);

        if (_process is { HasExited: false })
            _ownedPid = _process.Id;

        AppLog.Append($"[vpn] sing-box pid={_ownedPid?.ToString() ?? "elevated"}");
        if (_process?.StartInfo.RedirectStandardError == true)
            _ = ReadStderrAsync(_process, cancellationToken);

        await WaitForStartupAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        TryAssignOwnedProcess();
        if (_ownedPid is int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                AppLog.Append($"[vpn] stop pid={pid}: {ex.Message}");
            }
        }
        _process?.Dispose();
        _process = null;
        _ownedPid = null;
        await UplinkProbe.WaitForDiagPortClosedAsync();
    }

    private static Process StartDirect(string exe, string args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            WorkingDirectory = workDir,
        };
        psi.Environment["PATH"] = $"{workDir}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";
        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start sing-box");
    }

    private static Process? StartElevated(string exe, string args, string workDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            return Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223 || ex.HResult == UacCancelledHResult)
        {
            throw new InvalidOperationException(Strings.VpnUacCancelled);
        }
    }

    private Process? TryGetProcess()
    {
        if (_ownedPid is not int pid) return null;
        if (_process is { Id: var id, HasExited: false } && id == pid) return _process;
        _process?.Dispose();
        try
        {
            var proc = Process.GetProcessById(pid);
            if (proc.HasExited) { proc.Dispose(); return null; }
            _process = proc;
            return proc;
        }
        catch
        {
            return null;
        }
    }

    private void TryAssignOwnedProcess()
    {
        if (_ownedPid is not null) return;
        if (_process is { HasExited: false })
        {
            _ownedPid = _process.Id;
            return;
        }
        foreach (var proc in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                if (!proc.HasExited)
                {
                    _ownedPid = proc.Id;
                    _process = proc;
                    return;
                }
                proc.Dispose();
            }
            catch
            {
                proc.Dispose();
            }
        }
    }

    private async Task WaitForStartupAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ConnectTimeouts.StartupMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryAssignOwnedProcess();
            var proc = TryGetProcess();
            if (proc is { HasExited: true })
                throw new InvalidOperationException(FormatStartupError());

            var err = _stderr.Length > 0 ? _stderr.ToString() : ReadLogTail();
            if (ContainsFatal(err))
                throw new InvalidOperationException(FormatStartupError(err));

            if (await UplinkProbe.TcpReachableAsync(
                    SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, 500, cancellationToken))
                return;

            await Task.Delay(StartupPollMs, cancellationToken);
        }
        throw new InvalidOperationException(FormatStartupError());
    }

    private async Task ReadStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null) break;
                lock (_stderr)
                {
                    _stderr.AppendLine(line);
                    if (_stderr.Length > 8192)
                        _stderr.Remove(0, _stderr.Length - 4096);
                }
                AppLog.Append($"[sing-box] {line}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Append($"[sing-box stderr] {ex.Message}");
        }
    }

    private string ReadLogTail()
    {
        try
        {
            if (!File.Exists(_logPath)) return "";
            var text = File.ReadAllText(_logPath);
            return text.Length > 4096 ? text[^4096..] : text;
        }
        catch
        {
            return "";
        }
    }

    private static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string InjectLogOutput(string configJson, string logPath)
    {
        var node = JsonNode.Parse(configJson)?.AsObject();
        if (node is null) return configJson;
        if (node["log"] is JsonObject log)
            log["output"] = logPath;
        else
            node["log"] = new JsonObject { ["output"] = logPath };
        return node.ToJsonString();
    }

    private static bool ContainsFatal(string err) =>
        err.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
        || err.Contains("panic:", StringComparison.OrdinalIgnoreCase);

    private string FormatStartupError(string? err = null)
    {
        err ??= _stderr.Length > 0 ? _stderr.ToString() : ReadLogTail();
        if (err.Contains("wintun", StringComparison.OrdinalIgnoreCase))
            return "wintun.dll not found. Run: py -3 scripts/for_builds/singbox-build-windows.py";
        if (err.Contains("libcronet", StringComparison.OrdinalIgnoreCase)
            || err.Contains("cronet", StringComparison.OrdinalIgnoreCase))
            return "libcronet.dll not found. Run: py -3 scripts/for_builds/singbox-build-windows.py";
        if (err.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || err.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            return Strings.VpnTunAccessDenied;
        var tail = err.Trim();
        if (tail.Length > 200) tail = tail[^200..];
        return string.IsNullOrWhiteSpace(tail) ? "sing-box exited during startup" : tail;
    }

    private static string? FindSingBoxExe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "sing-box.exe"),
            Path.Combine(AppContext.BaseDirectory, "Native", "bin", "sing-box.exe"),
        };
        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null) return null;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), "sing-box.exe");
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
