using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services;

public enum AppUpdateState
{
    Idle,
    Available,
    Downloading,
    Ready,
    Applying,
}

/// <summary>Checks fastapi version + sha256, downloads Updater from fastapi, applies silent update.</summary>
public sealed class AppUpdateService : NotifyBase
{
    static readonly HttpClient Http = CreateHttp();

    static string UpdateDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "update");

    static string PendingPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DemarVpn", "pending_update");

    int _downloadInFlight;
    CancellationTokenSource? _loopCts;
    WindowsRelease? _release;

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

    string TargetPath(WindowsRelease r) => Path.Combine(UpdateDir, r.Asset);
    static string StampPath(string target) => target + ".sha256";
    static string PartialPath(string target) => target + ".partial";

    public bool TryApplyPending()
    {
        try
        {
            if (!File.Exists(PendingPath)) return false;
            var lines = File.ReadAllLines(PendingPath);
            var exe = lines.ElementAtOrDefault(0)?.Trim();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                ClearPendingFile();
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

    /// <summary>Reset state (unless downloading) after channel toggle.</summary>
    public void ClearPendingUpdate()
    {
        if (State != AppUpdateState.Downloading)
            State = AppUpdateState.Idle;
        ClearPendingFile();
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

        WindowsRelease? release;
        try
        {
            release = await WindowsReleaseApi.FetchAsync(UpdateChannelStore.PreferBeta, ct);
        }
        catch (Exception ex)
        {
            AppLog.Append($"update: version check failed — {ex.Message}");
            return;
        }

        if (release is null || !IsNewer(release.Version, Strings.VersionName))
        {
            ClearCache(keepAsset: null);
            State = AppUpdateState.Idle;
            return;
        }

        _release = release;
        LatestVersion = release.Version;

        if (CachedFile(release) is { })
        {
            State = AppUpdateState.Ready;
            return;
        }

        State = AppUpdateState.Available;

        if (Interlocked.Exchange(ref _downloadInFlight, 1) == 1) return;
        try
        {
            await DownloadAsync(release, ct);
        }
        finally
        {
            Interlocked.Exchange(ref _downloadInFlight, 0);
        }
    }

    async Task DownloadAsync(WindowsRelease release, CancellationToken ct)
    {
        State = AppUpdateState.Downloading;
        ProgressPercent = 0;
        var target = TargetPath(release);
        var partial = PartialPath(target);
        var stamp = StampPath(target);
        try
        {
            Directory.CreateDirectory(UpdateDir);
            if (File.Exists(target) && CachedFile(release) is null)
            {
                TryDelete(target);
                TryDelete(stamp);
            }

            // Drop empty leftovers from a stalled prior attempt.
            if (File.Exists(partial) && new FileInfo(partial).Length == 0)
                TryDelete(partial);

            Exception? last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await DownloadOnceAsync(release, target, partial, stamp, ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    AppLog.Append($"update: attempt {attempt + 1}/3 failed — {ex.Message}");
                }
            }

            AppLog.Append($"update: download failed — {last?.Message}");
            AppServices.Snackbar.ShowError(Strings.UpdateDownloadFail);
            State = AppUpdateState.Idle;
            ProgressPercent = 0;
        }
        catch (OperationCanceledException)
        {
            State = AppUpdateState.Idle;
            throw;
        }
    }

    async Task DownloadOnceAsync(
        WindowsRelease release, string target, string partial, string stamp, CancellationToken ct)
    {
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0L;
        if (offset > 0) ProgressPercent = Math.Max(ProgressPercent, 1);
        AppLog.Append($"update: download {release.Version} offset={offset}");

        using var req = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
        req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream,*/*");
        if (offset > 0) req.Headers.Range = new RangeHeaderValue(offset, null);

        using var response = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        var code = (int)response.StatusCode;
        if (code == 206) { /* resume */ }
        else if (response.IsSuccessStatusCode)
        {
            offset = 0L;
            TryDelete(partial);
        }
        else
        {
            throw new HttpRequestException($"HTTP {code}");
        }

        var total = ContentTotal(response, offset);
        const int idleMs = 45_000;

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create,
                       FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long read = offset;
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idleCts.CancelAfter(idleMs);
            int n;
            while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), idleCts.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                idleCts.CancelAfter(idleMs);
                if (total > 0)
                    ProgressPercent = (int)Math.Clamp(read * 100 / total, 0, 99);
            }

            if (read <= offset)
                throw new IOException("download stalled (0 bytes)");
        }

        if (!string.Equals(await Sha256FileAsync(partial, ct), release.Sha256, StringComparison.Ordinal))
        {
            TryDelete(partial);
            TryDelete(target);
            TryDelete(stamp);
            AppLog.Append("update: sha256 mismatch");
            AppServices.Snackbar.ShowError(Strings.UpdateHashMismatch);
            State = AppUpdateState.Idle;
            ProgressPercent = 0;
            return;
        }

        TryDelete(target);
        File.Move(partial, target);
        await File.WriteAllTextAsync(stamp, release.Sha256, ct);
        ProgressPercent = 100;
        LatestVersion = release.Version;
        _release = release;
        State = AppUpdateState.Ready;
        AppLog.Append($"update: ready {release.Version}");
    }

    string? CachedFile(WindowsRelease release)
    {
        var target = TargetPath(release);
        var stamp = StampPath(target);
        if (!File.Exists(target) || !File.Exists(stamp)) return null;
        try
        {
            var stamped = File.ReadAllText(stamp).Trim().ToLowerInvariant();
            return stamped == release.Sha256 ? target : null;
        }
        catch
        {
            return null;
        }
    }

    void ClearCache(string? keepAsset)
    {
        try
        {
            if (!Directory.Exists(UpdateDir)) return;
            foreach (var f in Directory.EnumerateFiles(UpdateDir))
            {
                var name = Path.GetFileName(f);
                if (keepAsset is not null && name.StartsWith(keepAsset, StringComparison.OrdinalIgnoreCase))
                    continue;
                TryDelete(f);
            }
        }
        catch { /* ignore */ }
    }

    static long ContentTotal(HttpResponseMessage response, long offset)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.HasLength == true && range.Length is > 0) return range.Length.Value;
            var len = response.Content.Headers.ContentLength ?? -1L;
            return len > 0 ? offset + len : -1L;
        }
        return response.Content.Headers.ContentLength ?? -1L;
    }

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void ScheduleNextLaunch()
    {
        var path = _release is not null ? TargetPath(_release) : null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(PendingPath)!);
        File.WriteAllText(PendingPath, $"{path}\n{LatestVersion ?? ""}\n");
        AppLog.Append($"update: scheduled for next launch ({LatestVersion})");
    }

    public bool ApplyNow()
    {
        var path = _release is not null ? TargetPath(_release) : null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = Directory.Exists(UpdateDir)
                ? Directory.EnumerateFiles(UpdateDir, "*.exe").FirstOrDefault()
                : null;
        }
        return path is not null && Apply(path);
    }

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
            ClearPendingFile();
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "/silent /launch",
                UseShellExecute = true,
            });
            AppLog.Append("update: launched Updater /silent /launch");
            AppServices.Vpn.EnsureStoppedSync();
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

    public static bool IsNewer(string latest, string local) => WindowsReleaseApi.IsNewer(latest, local);

    static void ClearPendingFile() => TryDelete(PendingPath);

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
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Demar-Client", DemarHttp.ClientId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DemarHttp.UserAgent);
        return client;
    }
}
