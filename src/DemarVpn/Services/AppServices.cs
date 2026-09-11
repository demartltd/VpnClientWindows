using DemarVpn.Models;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Servers;
using DemarVpn.Services.Vpn;

namespace DemarVpn.Services;

public static class AppServices
{
    public static VpnController Vpn { get; } = new();
    public static SnackbarService Snackbar { get; } = new();
    public static AuthSession Auth { get; } = new();
    public static AppUpdateService Update { get; } = new();

    public static string SingBoxLabel { get; private set; } = "1.13.13";

    private static CancellationTokenSource? _bgRefreshCts;

    public static void Init()
    {
        AppLog.Init();
        UpdateChannelStore.Init();
        RunAtStartup.Init();
        RunAtStartup.Sync();
        TunSettingsStore.Init();
        AuthSessionStore.Load();
        Auth.NotifyChanged();
        Vpn.HydrateFromCache();
        SingBoxLabel = ReadBundledVersion();
    }

    public static async Task BootstrapSessionAsync()
    {
        try
        {
            if (!AuthRepository.IsSignedIn)
            {
                await Vpn.RefreshTunGroupsAsync();
                return;
            }
            var ok = await AuthRepository.RefreshSessionAsync();
            Auth.NotifyChanged();
            if (ok) await Vpn.RefreshServersAsync();
            else await Vpn.OnSignedOutAsync();
        }
        catch (Exception ex)
        {
            AppLog.Append($"auth: bootstrap failed — {ex.Message}");
        }
    }

    /// <summary>One 10‑min loop: app update + session data — parity Apple AppState.startBackgroundRefresh.</summary>
    public static void StartBackgroundRefresh()
    {
        StopBackgroundRefresh();
        _bgRefreshCts = new CancellationTokenSource();
        _ = RunBackgroundRefreshAsync(_bgRefreshCts.Token);
    }

    public static void StopBackgroundRefresh()
    {
        _bgRefreshCts?.Cancel();
        _bgRefreshCts?.Dispose();
        _bgRefreshCts = null;
    }

    public static async Task SignOutFullyAsync()
    {
        await Auth.SignOutAsync();
        await Vpn.OnSignedOutAsync();
    }

    private static async Task RunBackgroundRefreshAsync(CancellationToken ct)
    {
        try
        {
            await TickBackgroundRefreshAsync(ct);
            using var timer = new PeriodicTimer(SessionRefreshPolicy.Ttl);
            while (await timer.WaitForNextTickAsync(ct))
                await TickBackgroundRefreshAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private static async Task TickBackgroundRefreshAsync(CancellationToken ct)
    {
        await Update.CheckAndDownloadAsync(ct);
        await Vpn.RefreshSessionDataIfStaleAsync(ct);
    }

    private static string ReadBundledVersion()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "singbox", "bundled_version.txt");
            if (!File.Exists(path))
                path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "singbox", "bundled_version.txt");
            if (File.Exists(path))
            {
                var v = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(v)) return v.TrimStart('v');
            }
        }
        catch { /* fallback */ }
        return "1.13.13";
    }
}

public static class SocialLinks
{
    public static IReadOnlyList<(SocialIcon Icon, string Url)> All { get; } =
    [
        (SocialIcon.X, "https://x.com/demargames"),
        (SocialIcon.Bluesky, "https://bsky.app/profile/demargames.bsky.social"),
        (SocialIcon.Instagram, "https://www.instagram.com/demargames/"),
        (SocialIcon.TikTok, "https://www.tiktok.com/@demargames"),
        (SocialIcon.Telegram, "https://t.me/demargames"),
    ];
}
