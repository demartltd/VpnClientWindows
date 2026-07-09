namespace DemarVpn.Domain.Vpn;

public static class ConnectTimeouts
{
    public const int PreflightMs = 2_000;
    public const int StartupMs = 3_000;
    public const int ProbeMs = 8_000;
    public const int ConnectMs = 15_000;
    public const int WatchdogMs = ConnectMs + 2_000;
}
