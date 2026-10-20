namespace DemarVpn.Domain.Vpn;

public enum ServerReachStatus
{
    Checking,
    Online,
    Offline,
    Degraded,
}

public sealed record ServerReachState(
    ServerReachStatus Status,
    int? LatencyMs = null,
    int? TlsMs = null)
{
    public static ServerReachState Checking { get; } = new(ServerReachStatus.Checking);
    public static ServerReachState Offline { get; } = new(ServerReachStatus.Offline);

    public static string FormatMs(int? ms) => ms is int v ? $"~{v}ms" : "n/a";

    public static ServerReachState FromProbes(int tcpMs, int tlsMs)
    {
        var tcpOk = tcpMs >= 0;
        var tlsOk = tlsMs >= 0;
        if (!tcpOk && !tlsOk) return Offline;
        return new ServerReachState(
            ServerReachStatus.Online,
            tcpOk ? tcpMs : null,
            tlsOk ? tlsMs : null);
    }
}
