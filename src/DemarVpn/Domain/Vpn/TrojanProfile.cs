namespace DemarVpn.Domain.Vpn;

public sealed record TrojanProfile(
    string DisplayName,
    string Server,
    int Port,
    string Password,
    string TlsServerName) : IProxyProfile
{
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Password);
    public ServerProtocol Protocol => ServerProtocol.Trojan;
    public bool PreferResolvedDial => true;

    public SingBoxConnectPlan ToConnectPlan(IReadOnlyList<ApiBypassRuleSet>? bypass = null) =>
        SingBoxConnectPlan.Trojan(this, bypass);
}
