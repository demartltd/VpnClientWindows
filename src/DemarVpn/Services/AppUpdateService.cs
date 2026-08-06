using System.Diagnostics;
using System.Net.Http;

namespace DemarVpn.Services;

public enum AppUpdateState
{
    Idle,
    Available,
    Downloading,
    Ready,
    Applying,
}

/// <summary>Checks API version, downloads Updater.exe, applies silent update.</summary>
public sealed class AppUpdateService : NotifyBase
{
    public const string UpdaterUrl = "https://vpn.demar.cc/Updater.exe";

    static readonly HttpClient Http = CreateHttp();

    static string UpdateDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "update");

    static string UpdaterPath => Path.Combine(UpdateDir, "Updater.exe");
    static string PartialPath => Path.Combine(UpdateDir, "Updater.exe.partial");
    static string PendingPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "pending_update");

    int _downloadInFlight;
    CancellationTokenSource? _loopCts;

    AppUpdateState _state = AppUpdateState.Idle;
    int _progressPercent;
    string? _latestVersion;

    public AppUpdateState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => Set(ref _progressPercent, value);
    }

    public string? LatestVersion
    {
        get => _latestVersion;
        private set => Set(ref _latestVersion, value);
    }

    public bool TryApplyPending()
    {
        try
        {
            if (!File.Exists(PendingPath)) return false;
            var lines = File.ReadAllLines(PendingPath);
            var exe = lines.ElementAtOrDefault(0)?.Trim();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                ClearPending();
                return false;
            }

            LatestVersion = lines.ElementAtOrDefault(1)?.Trim();
            return Apply(exe);
        }
        catch (Exception ex)
        {
            AppLog.Append($"update: pending apply failed — {ex.Message}");
            return false;
        }
    }

    public void StartPeriodicChecks()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = new CancellationTokenSource();
        _ = RunPeriodicAsync(_loopCts.Token);
    }

    public void StopPeriodicChecks()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    async Task RunPeriodicAsync(CancellationToken ct)
    {
        await CheckAndDownloadAsync(ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await CheckAndDownloadAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    public async Task CheckAndDownloadAsync(CancellationToken ct = default)
    {
        if (State is AppUpdateState.Downloading or AppUpdateState.Applying) return;

        string? latest;
        try
        {
            latest = await WindowsReleaseApi.FetchLatestVersionAsync(ct);
        }
        catch (Exception ex)
        {
            AppLog.Append($"update: version check failed — {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(latest) || !IsNewer(latest, Strings.VersionName))
            return;

        if (State == AppUpdateState.Ready
            && string.Equals(LatestVersion, latest, StringComparison.OrdinalIgnoreCase)
            && File.Exists(UpdaterPath))
            return;

        LatestVersion = latest;
        State = AppUpdateState.Available;

        if (Interlocked.Exchange(ref _downloadInFlight, 1) == 1) return;
        try
        {
            await DownloadAsync(latest, ct);
        }
        finally
        {
            Interlocked.Exchange(ref _downloadInFlight, 0);
        }
    }

    async Task DownloadAsync(string version, CancellationToken ct)
    {
        State = AppUpdateState.Downloading;
        ProgressPercent = 0;
        try
        {
            Directory.CreateDirectory(UpdateDir);
            if (File.Exists(PartialPath)) File.Delete(PartialPath);

            using var response = await Http.GetAsync(UpdaterUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;

            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(PartialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0)
                        ProgressPercent = (int)Math.Clamp(read * 100 / total, 0, 100);
                }
            }

            if (File.Exists(UpdaterPath)) File.Delete(UpdaterPath);
            File.Move(PartialPath, UpdaterPath);
            ProgressPercent = 100;
            LatestVersion = version;
            State = AppUpdateState.Ready;
            AppLog.Append($"update: downloaded {version}");
        }
        catch (OperationCanceledException)
        {
            State = AppUpdateState.Idle;
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Append($"update: download failed — {ex.Message}");
            TryDelete(PartialPath);
            State = AppUpdateState.Idle;
            ProgressPercent = 0;
        }
    }

    public void ScheduleNextLaunch()
    {
        if (!File.Exists(UpdaterPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(PendingPath)!);
        File.WriteAllText(PendingPath, $"{UpdaterPath}\n{LatestVersion ?? ""}\n");
        AppLog.Append($"update: scheduled for next launch ({LatestVersion})");
    }

    public bool ApplyNow() => Apply(UpdaterPath);

    bool Apply(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            AppLog.Append("update: apply — updater missing");
            return false;
        }

        try
        {
            State = AppUpdateState.Applying;
            ClearPending();
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "/silent /launch",
                UseShellExecute = true,
            });
            AppLog.Append("update: launched Updater /silent /launch");
            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Append($"update: apply failed — {ex.Message}");
            State = AppUpdateState.Ready;
            return false;
        }
    }

    public static bool IsNewer(string latest, string local)
    {
        if (!TryParseVersion(latest, out var remote)) return false;
        if (!TryParseVersion(local, out var current)) return true;
        return remote > current;
    }

    static bool TryParseVersion(string raw, out Version version)
    {
        version = new Version(0, 0);
        var s = raw.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(s)) return false;
        // Version needs at least Major.Minor
        var parts = s.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out var major))
        {
            version = new Version(major, 0);
            return true;
        }
        return Version.TryParse(s, out version!);
    }

    static void ClearPending()
    {
        TryDelete(PendingPath);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* ignore */ }
    }

    static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
    }
}
