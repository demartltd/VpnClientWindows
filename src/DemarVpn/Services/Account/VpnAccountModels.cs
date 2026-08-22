namespace DemarVpn.Services.Account;

public sealed record VpnAccount(
    bool NotifyNews,
    bool NotifyImportant,
    string? VpnSubscription,
    string? VpnExpiresAt);

public sealed record VpnNotify(bool NotifyNews, bool NotifyImportant);

public sealed record VpnTrafficServer(string Name, long Bytes);

public sealed record VpnTraffic(
    int Y,
    int M,
    IReadOnlyList<VpnTrafficServer> Servers,
    long Total,
    long LimitBytes)
{
    public bool IsBlocked => LimitBytes > 0 && Total >= LimitBytes;
}

public sealed record PromoResult(string Result, string Message, string? Subscription, string? ExpiresAt)
{
    public bool IsActivated => Result == "activated";

    public string LocalizedMessage => Result switch
    {
        "activated" => Strings.PromoActivated,
        "not_found" => Strings.PromoNotFound,
        "already_used" => Strings.PromoAlreadyUsed,
        "blocked_by_other" => Strings.PromoBlocked,
        "expired" => Strings.PromoExpired,
        _ => Message,
    };
}
