namespace DemarVpn.Domain.Vpn;

public enum ServerReachStatus
{
    Checking,
    Online,
    Offline,
    Degraded,
}

public sealed record ServerReachState(ServerReachStatus Status);
