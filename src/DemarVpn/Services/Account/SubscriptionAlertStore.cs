using DemarVpn.Services;

namespace DemarVpn.Services.Account;

/// <summary>Cooldown / window rules for subscription upsell sheets (parity Apple/Android).</summary>
public static class SubscriptionAlertStore
{
    public const long TrafficThresholdBytes = 200L * 1024 * 1024;
    public const long ExpiryEarlyMaxMs = 72L * 60 * 60 * 1000;
    public const long ExpiryEarlyMinMs = 24L * 60 * 60 * 1000;
    public const long ExpiryFinalMaxMs = 18L * 60 * 60 * 1000;
    public const long ExpiryCooldownMs = 3L * 60 * 60 * 1000;
    public const long TrafficCooldownMs = 6L * 60 * 60 * 1000;

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn");
    static readonly string ExpiryPath = Path.Combine(Dir, "sub_alert_expiry_at");
    static readonly string TrafficPath = Path.Combine(Dir, "sub_alert_traffic_at");

    public static bool CanShowExpiry(DateTimeOffset? now = null) =>
        CanShow(ExpiryPath, ExpiryCooldownMs, now);

    public static void MarkExpiryShown(DateTimeOffset? now = null) =>
        MarkShown(ExpiryPath, now);

    public static bool CanShowTraffic(DateTimeOffset? now = null) =>
        CanShow(TrafficPath, TrafficCooldownMs, now);

    public static void MarkTrafficShown(DateTimeOffset? now = null) =>
        MarkShown(TrafficPath, now);

    public static long? MillisUntilExpiry(string expiresAtIso, DateTimeOffset? now = null)
    {
        if (!DateTimeOffset.TryParse(expiresAtIso, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var exp))
            return null;
        return (long)(exp - (now ?? DateTimeOffset.UtcNow)).TotalMilliseconds;
    }

    public static bool ShouldShowExpiry(string expiresAtIso, DateTimeOffset? now = null)
    {
        var ms = MillisUntilExpiry(expiresAtIso, now);
        if (ms is null or <= 0) return false;
        // (24h, 72h] early · (18h, 24h] silent · (0, 18h] final
        return (ms > ExpiryEarlyMinMs && ms <= ExpiryEarlyMaxMs)
            || (ms >= 1 && ms <= ExpiryFinalMaxMs);
    }

    public static bool ShouldShowTraffic(long usedBytes) => usedBytes >= TrafficThresholdBytes;

    /// <summary>Hours if ≥1h, otherwise minutes (at least 1).</summary>
    public static (bool Hours, int Amount) RemainingTime(long ms)
    {
        var safe = Math.Max(ms, 1L);
        if (safe >= 3_600_000L)
            return (true, Math.Max((int)(safe / 3_600_000L), 1));
        return (false, Math.Max((int)(safe / 60_000L), 1));
    }

    public static string FormatRemaining(long ms)
    {
        var (hours, amount) = RemainingTime(ms);
        var fmt = hours
            ? PickPlural(amount, Strings.SubAlertInHoursOne, Strings.SubAlertInHoursFew, Strings.SubAlertInHoursMany)
            : PickPlural(amount, Strings.SubAlertInMinutesOne, Strings.SubAlertInMinutesFew, Strings.SubAlertInMinutesMany);
        return string.Format(fmt, amount);
    }

    static string PickPlural(int n, string one, string few, string many)
    {
        if (!LocaleStore.UsesSlavicPlurals)
            return n == 1 ? one : many;
        var m100 = Math.Abs(n) % 100;
        var m10 = m100 % 10;
        if (m100 is >= 11 and <= 14) return many;
        if (m10 == 1) return one;
        if (m10 is >= 2 and <= 4) return few;
        return many;
    }

    static bool CanShow(string path, long cooldownMs, DateTimeOffset? now)
    {
        var last = ReadAt(path);
        if (last <= 0) return true;
        var t = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        return t - last >= cooldownMs;
    }

    static void MarkShown(string path, DateTimeOffset? now)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds().ToString());
        }
        catch { /* ignore */ }
    }

    static long ReadAt(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            return long.TryParse(File.ReadAllText(path).Trim(), out var v) ? v : 0;
        }
        catch { return 0; }
    }
}
