using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Network;

namespace DemarVpn.Services.Vpn;

public sealed class SingBoxProcessEngine : IVpnEngine
{
    private const int UacCancelledHResult = unchecked((int)0x800704C7);
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;
    private int? _ownedPid;
    private ProcessJobLink? _job;
    private readonly string _configPath;
    private readonly string _logPath;
    private readonly string _cachePath;
    private readonly string _startupLogPath;

    public event Action? CoreExited;

    public SingBoxProcessEngine()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "demar");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "config.json");
        _logPath = Path.Combine(dir, "sing-box.log");
        _cachePath = Path.Combine(dir, "cache.db");
        _startupLogPath = CoreWatchEntry.StartupLogPath;
    }

    public async Task StartAsync(string configJson, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopLocal();
            await StopElevatedIfNeededAsync().ConfigureAwait(false);
            TryDelete(_logPath);
            TryDelete(_startupLogPath);

            configJson = InjectRuntimePaths(configJson, _logPath, _cachePath);
            await File.WriteAllTextAsync(_configPath, configJson, cancellationToken).ConfigureAwait(false);

            var exe = FindSingBoxExe();
            if (exe is null)
                throw new InvalidOperationException(
                    "sing-box.exe not found. Run: py -3 scripts/for_builds/singbox-build-windows.py");

            var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            var coreArgv = new[] { "run", "-c", _configPath };
            if (IsAdmin())
            {
                _process = StartDirect(exe, coreArgv, workDir);
                _ownedPid = _process.Id;
                WireExited(_process);
            }
            else
            {
                StartElevatedWatch(exe, coreArgv);
                _ownedPid = null;
            }

            AppLog.Append($"[vpn] sing-box pid={DescribePid()}");
            await WaitForDiagPortAsync(cancellationToken).ConfigureAwait(false);
            _ownedPid ??= CoreProcess.FirstPid();
            if (_ownedPid is int pid && _process is null && CoreProcess.IsRunning(pid))
            {
                try
                {
                    _process = Process.GetProcessById(pid);
                    WireExited(_process);
                }
                catch
                {
                    _ownedPid = pid;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        if (!await _gate.WaitAsync(GateTimeout).ConfigureAwait(false))
        {
            AppLog.Append("[vpn] stop: gate timeout — best-effort kill");
            try { StopLocal(); } catch { /* ignore */ }
            try { CoreProcess.KillAll(); } catch { /* ignore */ }
            return;
        }

        try
        {
            StopLocal();
            await StopElevatedIfNeededAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StopLocal()
    {
        if (_ownedPid is int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                AppLog.Append($"[vpn] stop pid={pid}: {ex.Message}");
            }
        }

        CoreProcess.KillAll();
        ReleaseProcessHandle();
        _job?.Dispose();
        _job = null;
    }

    private static async Task StopElevatedIfNeededAsync()
    {
        var diagUp = await UplinkProbe.TcpReachableAsync(
            SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, 150).ConfigureAwait(false);
        if (!CoreProcess.AnyRunning() && !diagUp)
            return;

        TryElevatedStop();
        await UplinkProbe.WaitForDiagPortClosedAsync().ConfigureAwait(false);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!CoreProcess.AnyRunning()
                && !await UplinkProbe.TcpReachableAsync(
                    SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, 150).ConfigureAwait(false))
                return;
            await Task.Delay(100).ConfigureAwait(false);
        }

        AppLog.Append("[vpn] elevated stop: timeout");
    }

    private static void TryElevatedStop()
    {
        if (IsAdmin())
        {
            CoreProcess.KillAll();
            WintunCleanup.RemoveAll();
            return;
        }

        var appExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(appExe)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = appExe,
                Arguments = CoreStopEntry.Flag,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223 || ex.HResult == UacCancelledHResult) { }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] elevated stop: {ex.Message}");
        }
    }

    private void ReleaseProcessHandle()
    {
        DropProcessHandle();
        _ownedPid = null;
    }

    private void DropProcessHandle()
    {
        if (_process is null) return;
        try { _process.Dispose(); }
        catch (InvalidOperationException) { }
        _process = null;
    }

    private Process StartDirect(string exe, IReadOnlyList<string> coreArgv, string workDir)
    {
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

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start sing-box");
        try
        {
            _job?.Dispose();
            _job = new ProcessJobLink();
            _job.Assign(process);
        }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] job link: {ex.Message}");
        }

        return process;
    }

    private static void StartElevatedWatch(string exe, IReadOnlyList<string> coreArgv)
    {
        var appExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(appExe))
            throw new InvalidOperationException("Process path unknown");

        var parts = new List<string>
        {
            CoreWatchArgs.Flag,
            Environment.ProcessId.ToString(),
            "--",
            QuoteArg(exe),
        };
        foreach (var arg in coreArgv)
            parts.Add(QuoteArg(arg));

        try
        {
            if (Process.Start(new ProcessStartInfo
                {
                    FileName = appExe,
                    Arguments = string.Join(' ', parts),
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                }) is null)
                throw new InvalidOperationException("Failed to start elevated core watch");
        }
        catch (Win32Exception ex)
        {
            AppLog.Append($"[vpn] elevate watch: {ex.NativeErrorCode} {ex.Message}");
            if (ex.NativeErrorCode == 1223 || ex.HResult == UacCancelledHResult)
                throw new InvalidOperationException(Strings.VpnUacCancelled);
            throw new InvalidOperationException(Strings.VpnTunAccessDenied);
        }
    }

    private string DescribePid() => _ownedPid?.ToString() ?? "watch";

    private static string QuoteArg(string arg)
    {
        if (!arg.Contains(' ') && !arg.Contains('"'))
            return arg;
        return $"\"{arg.Replace("\"", "\\\"")}\"";
    }

    private void WireExited(Process process)
    {
        if (!process.EnableRaisingEvents)
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => CoreExited?.Invoke();
        }
    }

    private async Task WaitForDiagPortAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ConnectTimeouts.StartupMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var err = CollectStartupErrors();
            if (ContainsFatal(err))
                throw new InvalidOperationException(FormatStartupError(err));

            if (_ownedPid is int pid && !CoreProcess.IsRunning(pid) && CoreProcess.AnyRunning())
                _ownedPid = CoreProcess.FirstPid();

            if (await UplinkProbe.TcpReachableAsync(
                    SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, 500, cancellationToken)
                .ConfigureAwait(false))
            {
                AppLog.Append("[vpn] diag port ready");
                return;
            }

            await Task.Delay(ConnectTimeouts.StartupPollMs, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(FormatStartupError(CollectStartupErrors()));
    }

    private string CollectStartupErrors() =>
        ReadFileTail(_logPath) + ReadFileTail(_startupLogPath);

    private static string ReadFileTail(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";
            var text = File.ReadAllText(path);
            return text.Length > 4096 ? text[^4096..] : text;
        }
        catch
        {
            return "";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignore */ }
    }

    private static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string InjectRuntimePaths(string configJson, string logPath, string cachePath)
    {
        var node = JsonNode.Parse(configJson)?.AsObject();
        if (node is null) return configJson;
        if (node["log"] is JsonObject log)
            log["output"] = logPath;
        else
            node["log"] = new JsonObject { ["output"] = logPath };
        if (node["experimental"] is not JsonObject experimental)
            node["experimental"] = experimental = new JsonObject();
        if (experimental["cache_file"] is not JsonObject cache)
            experimental["cache_file"] = cache = new JsonObject { ["enabled"] = true, ["store_dns"] = true };
        cache["path"] = cachePath;
        return node.ToJsonString();
    }

    private static bool ContainsFatal(string err) =>
        err.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
        || err.Contains("panic:", StringComparison.OrdinalIgnoreCase)
        || err.Contains("access is denied", StringComparison.OrdinalIgnoreCase);

    private string FormatStartupError(string? err = null)
    {
        err ??= CollectStartupErrors();
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
        return string.IsNullOrWhiteSpace(tail)
            ? $"{Strings.VpnConnectFailed}: diag port unreachable"
            : tail;
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
