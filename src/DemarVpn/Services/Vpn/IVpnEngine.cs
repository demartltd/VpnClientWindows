namespace DemarVpn.Services.Vpn;

public interface IVpnEngine
{
    event Action? CoreExited;
    Task StartAsync(string configJson, CancellationToken cancellationToken);
    Task StopAsync();
}
