#if HAS_LIBBOX
using DemarVpn.Domain.Vpn;
using SagerNet;

namespace DemarVpn.Services.Vpn;

public sealed class LibboxVpnEngine : IVpnEngine, ICommandServerHandler
{
    private readonly long _owner = Interlocked.Increment(ref _nextOwner);
    private static long _nextOwner;
    private CommandServer? _server;
    private readonly WintunPlatform _platform = new();

    public bool IsRunning => _server is not null;

    public Task StartAsync(string configJson, CancellationToken cancellationToken)
    {
        StopCore();
        Libbox.Setup();
        _server = Libbox.NewCommandServer(this, _platform);
        _server.Start();
        _server.StartOrReloadService(configJson, new OverrideOptions());
        AppLog.Append("[vpn] libbox started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCore();
        _platform.CloseTun();
        return Task.CompletedTask;
    }

    private void StopCore()
    {
        _server?.CloseService();
        _server?.Close();
        _server = null;
    }

    public void ServiceStop() => _ = StopAsync();
    public void ServiceReload() { }
    public SystemProxyStatus? GetSystemProxyStatus() => null;
    public void SetSystemProxyEnabled(bool enabled) { }
}
#endif
