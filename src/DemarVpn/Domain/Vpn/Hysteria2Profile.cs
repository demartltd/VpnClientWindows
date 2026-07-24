namespace DemarVpn.Domain.Vpn;

public sealed record Hysteria2Profile(
    string DisplayName,
    string Server,
    int Port,
    string Password,
    string TlsServerName,
    IReadOnlyList<string>? Alpn = null,
    string? ObfsPassword = null)
{
    public IReadOnlyList<string> ResolvedAlpn => Alpn is { Count: > 0 } ? Alpn : ["h3"];
}
