using DemarVpn.Domain.Vpn;

namespace DemarVpn.Core.Servers;

public static class TestServer
{
    public static readonly IReadOnlyList<ServerProfile> All =
    [
        ServerProfile.NaiveFromTag(
            new("FI-DEV-99", "backronym.ru", 8443,
                "u_e06786d0488ce48f", "REDACTED", "backronym.ru")),
        ServerProfile.NaiveFromTag(
            new("SE-FREE-71", "swe-f-1.backronym.ru", 8443,
                "u_7e870100ee3ef828", "REDACTED", "swe-f-1.backronym.ru")),
        ServerProfile.NaiveFromTag(
            new("GR-FREE-22", "ne-f-1.backronym.ru", 8443,
                "u_0dd19c46df7fc81a", "REDACTED", "ne-f-1.backronym.ru")),
        ServerProfile.NaiveFromTag(
            new("DE-FREE-1", "de-f-1.backronym.ru", 8443,
                "u_5f0d9beeab98bf6c", "REDACTED", "de-f-1.backronym.ru")),
    ];

    public static ServerProfile Default => All[0];
}
