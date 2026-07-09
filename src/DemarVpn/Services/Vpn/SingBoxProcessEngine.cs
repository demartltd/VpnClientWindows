using System.Diagnostics;

namespace DemarVpn.Services.Vpn;

public sealed class SingBoxProcessEngine : IVpnEngine
{
    private Process? _process;
    private readonly string _configPath;

    public SingBoxProcessEngine()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "demar");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "config.json");
    }

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(string configJson, CancellationToken cancellationToken)
    {
        await StopAsync();
        await File.WriteAllTextAsync(_configPath, configJson, cancellationToken);
        var exe = FindSingBoxExe();
        if (exe is null)
            throw new InvalidOperationException(
                "sing-box.exe not found. Run: py -3 scripts/for_builds/libbox-build-windows.py");

        var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"run -c \"{_configPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            WorkingDirectory = workDir,
        };
        psi.Environment["PATH"] = $"{workDir}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";
        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start sing-box");
        AppLog.Append($"[vpn] sing-box pid={_process.Id}");
        _ = Task.Run(async () =>
        {
            if (_process is null) return;
            var err = await _process.StandardError.ReadToEndAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(err))
                AppLog.Append($"[sing-box stderr] {err.TrimEnd()}");
        }, cancellationToken);
    }

    public Task StopAsync()
    {
        if (_process is null) return Task.CompletedTask;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] stop: {ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
        return Task.CompletedTask;
    }

    private static string? FindSingBoxExe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "sing-box.exe"),
            Path.Combine(AppContext.BaseDirectory, "Native", "bin", "sing-box.exe"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "sing-box.exe"),
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
