namespace DemarVpn.Services.Servers;

/// <summary>Session data TTL — parity Apple SessionRefreshPolicy (600s).</summary>
public static class SessionRefreshPolicy
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public static bool IsStale(DateTimeOffset? last, DateTimeOffset? now = null)
    {
        if (last is null) return true;
        return (now ?? DateTimeOffset.UtcNow) - last.Value >= Ttl;
    }
}
