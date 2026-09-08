namespace DemarVpn.Domain.Vpn;

public static class ConnectTimeouts
{
    public const int ConnectMs = 35_000;
    public const int WatchdogMs = ConnectMs + 2_000;
    public const int StartupMs = 10_000;
    public const int StartupPollMs = 100;
    public const int ProbeMs = 3_000;
    public const int ProbeAttemptMs = 900;
    public const int HealthIntervalMs = 25_000;
    public const int HealthFailLimit = 2;
    public const int SwitchSettleMs = 100;
    public const int DiagPortCloseMs = 1_200;
}
