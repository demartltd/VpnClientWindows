namespace DemarVpn.Domain.Vpn;

public interface IProxyProfile
{
    string DisplayName { get; }
    string Server { get; }
    int Port { get; }
    bool HasCredentials { get; }
    ServerProtocol Protocol { get; }
    /// <summary>Dial by pre-resolved IPv4 (avoid DNS race inside TUN).</summary>
    bool PreferResolvedDial { get; }

    SingBoxConnectPlan ToConnectPlan(IReadOnlyList<ApiBypassRuleSet>? bypass = null);
}
