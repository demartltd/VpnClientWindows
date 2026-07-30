namespace DemarVpn.Services.Vpn;

public interface IVpnEngine
{
    bool IsRunning { get; }
    Task StartAsync(string configJson, CancellationToken cancellationToken);
    Task StopAsync();
}
