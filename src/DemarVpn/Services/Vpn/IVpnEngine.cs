namespace DemarVpn.Services.Vpn;

public interface IVpnEngine
{
    bool IsRunning { get; }
    Task StartAsync(string configJson, CancellationToken cancellationToken);
    Task StopAsync();
}

public static class VpnEngineFactory
{
    public static IVpnEngine Create() =>
#if HAS_LIBBOX
        new LibboxVpnEngine();
#else
        new SingBoxProcessEngine();
#endif
}
