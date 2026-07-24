using DemarVpn.Models;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Vpn;

namespace DemarVpn.Services;

public static class AppServices
{
    public static VpnController Vpn { get; } = new();
    public static SnackbarService Snackbar { get; } = new();
    public static AuthSession Auth { get; } = new();

    public static string SingBoxLabel { get; private set; } = "1.13.13";

    public static void Init()
    {
        AppLog.Init();
        AuthSessionStore.Load();
        Auth.NotifyChanged();
        SingBoxLabel = ReadBundledVersion();
    }

    public static async Task BootstrapSessionAsync()
    {
        try
        {
            if (!AuthRepository.IsSignedIn) return;
            var ok = await AuthRepository.RefreshSessionAsync();
            Auth.NotifyChanged();
            if (ok) await Vpn.RefreshServersAsync();
            else Vpn.ClearServers();
        }
        catch (Exception ex)
        {
            AppLog.Append($"auth: bootstrap failed — {ex.Message}");
        }
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
        (SocialIcon.Facebook, "https://www.facebook.com/demargames/"),
        (SocialIcon.Reddit, "https://reddit.com/r/demargames"),
        (SocialIcon.Telegram, "https://t.me/demargames"),
    ];
}
