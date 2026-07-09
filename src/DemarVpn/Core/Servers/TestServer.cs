using DemarVpn.Domain.Vpn;

namespace DemarVpn.Core.Servers;

public static class TestServer
{
    private static readonly NaiveProfile TestNaive = new(
        DisplayName: "backronym.ru",
        Server: "193.35.17.5",
        Port: 443,
        Username: "u_e06786d0488ce48f",
        Password: "REDACTED",
        TlsServerName: "backronym.ru");

    public static readonly ServerProfile Default = ServerProfile.FromNaive(TestNaive);
    public static IReadOnlyList<ServerProfile> All { get; } = [Default];
}
