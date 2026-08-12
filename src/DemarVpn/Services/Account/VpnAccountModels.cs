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
    long LimitBytes);
