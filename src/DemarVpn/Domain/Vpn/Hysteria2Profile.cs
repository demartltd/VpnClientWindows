namespace DemarVpn.Domain.Vpn;

public sealed record Hysteria2Profile(
    string DisplayName,
    string Server,
    int Port,
    string Password,
    string TlsServerName,
    IReadOnlyList<string>? Alpn = null,
    string? ObfsPassword = null,
    int? UpMbps = null,
    int? DownMbps = null) : IProxyProfile
{
    public IReadOnlyList<string> ResolvedAlpn => Alpn is { Count: > 0 } ? Alpn : ["h3"];
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Password);
    public ServerProtocol Protocol => ServerProtocol.Hysteria2;
    public bool PreferResolvedDial => false;

    public SingBoxConnectPlan ToConnectPlan(IReadOnlyList<ApiBypassRuleSet>? bypass = null) =>
        SingBoxConnectPlan.Hysteria2(this, bypass);
}
