namespace DemarVpn.Domain.Vpn;

public sealed record NaiveProfile(
    string DisplayName,
    string Server,
    int Port,
    string Username,
    string Password,
    string TlsServerName);
