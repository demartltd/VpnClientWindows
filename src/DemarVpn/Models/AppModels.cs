namespace DemarVpn.Models;

public enum VpnStateKind { Disconnected, Connecting, Connected, Disconnecting, Error }

public sealed record VpnState(VpnStateKind Kind, string Message = "")
{
    public static VpnState Disconnected { get; } = new(VpnStateKind.Disconnected);
    public static VpnState Connecting { get; } = new(VpnStateKind.Connecting);
    public static VpnState Connected { get; } = new(VpnStateKind.Connected);
    public static VpnState Disconnecting { get; } = new(VpnStateKind.Disconnecting);
    public static VpnState Error(string message) => new(VpnStateKind.Error, message);
    public bool IsBusy => Kind is VpnStateKind.Connecting or VpnStateKind.Disconnecting;
}

public sealed record SignedInUser(string Username, string Id, string VpnSubscription, string? VpnExpiresAt);

public sealed record ThroughputRates(double DownloadBps, double UploadBps);

public sealed record SnackMessage(SnackKind Kind, string Text, DateTimeOffset At);

public enum SnackKind { Info, Success, Error, Warning }
