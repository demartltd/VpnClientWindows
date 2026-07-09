using DemarVpn.Domain.Vpn;

namespace DemarVpn.Core.Servers;

public static class TestServer
{
    public static readonly IReadOnlyList<ServerProfile> All =
    [
        ServerProfile.Naive(
            new("FIN-DEV-1", "193.35.17.5", 443,
                "u_e06786d0488ce48f", "REDACTED", "backronym.ru"),
            "FI", "Финляндия"),
        ServerProfile.Naive(
            new("DE-FREE-1", "de-f-1.backronym.ru", 443,
                "u_5f0d9beeab98bf6c", "REDACTED", "de-f-1.backronym.ru"),
            "DE", "Германия"),
    ];

    public static ServerProfile Default => All[0];
}
