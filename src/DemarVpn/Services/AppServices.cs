using DemarVpn.Models;
using DemarVpn.Services.Vpn;

namespace DemarVpn.Services;

public static class AppServices
{
    public static VpnController Vpn { get; } = new();
    public static SnackbarService Snackbar { get; } = new();

    public static string SingBoxLabel { get; private set; } = "1.13.13";

    public static void Init()
    {
        AppLog.Init();
        SingBoxLabel = ReadBundledVersion();
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

public static class UiMocks
{
    public static Models.SignedInUser SignedInUser { get; } = new(
        "demo_user", "usr_demo", "active", "2026-12-31");
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
