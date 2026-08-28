namespace DemarVpn.Domain.Vpn;

public static class ConnectTimeouts
{
    public const int ConnectMs = 40_000;
    public const int WatchdogMs = ConnectMs + 2_000;
    public const int HealthIntervalMs = 25_000;
    public const int HealthFailLimit = 2;
    public const int SwitchSettleMs = 300;
    public const int DiagPortCloseMs = 2_000;
}
