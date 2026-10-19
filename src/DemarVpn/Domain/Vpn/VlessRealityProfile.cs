namespace DemarVpn.Domain.Vpn;

public sealed record VlessRealityProfile(
    string DisplayName,
    string Server,
    int Port,
    string Uuid,
    string TlsServerName,
    string PublicKey,
    string ShortId,
    string? Flow = "xtls-rprx-vision") : IProxyProfile
{
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Uuid)
        && !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(ShortId);

    public ServerProtocol Protocol => ServerProtocol.VlessReality;
    public bool PreferResolvedDial => true;

    public SingBoxConnectPlan ToConnectPlan(IReadOnlyList<ApiBypassRuleSet>? bypass = null) =>
        SingBoxConnectPlan.VlessReality(this, bypass);
}
