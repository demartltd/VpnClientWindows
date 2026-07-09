using DemarVpn.Core.Servers;
using DemarVpn.Domain.Vpn;
using DemarVpn.Models;
using DemarVpn.Services.Network;

namespace DemarVpn.Services.Vpn;

public sealed class VpnController : NotifyBase
{
    private readonly IVpnEngine _engine = VpnEngineFactory.Create();
    private CancellationTokenSource? _op;
    private CancellationTokenSource? _watchdog;
    private readonly List<IDisposable> _subs = [];

    public VpnState State { get; private set; } = VpnState.Disconnected;
    public ServerProfile Selected { get; private set; } = TestServer.Default;
    public ThroughputRates Throughput { get; private set; } = new(0, 0);
    public IReadOnlyList<ServerProfile> Servers => TestServer.All;

    public event Action<string>? ErrorRaised;

    public VpnController()
    {
        _subs.Add(VpnEventStream.AddListener(ApplyEvent));
    }

    public void Select(ServerProfile profile)
    {
        Selected = profile;
        Raise(nameof(Selected));
        AppLog.Append($"[ui] server selected: {profile.DisplayName}");
    }

    public async Task ConnectAsync()
    {
        if (State.IsBusy || State.Kind == VpnStateKind.Connected) return;
        _op?.Cancel();
        _watchdog?.Cancel();
        _op = new CancellationTokenSource();
        var token = _op.Token;
        SetState(VpnState.Connecting);
        AppLog.Append("[ui] windows home: connect");
        try
        {
            var plan = Selected.ConnectPlan;
            var resolved = await UplinkIpv4.ResolveAsync(plan.UplinkHost);
            if (resolved.Count > 0) plan = plan.WithExtraDirectIps(resolved);
            var configJson = SingBoxConfigBuilder.BuildTunConfigJson(plan);
            AppLog.Append($"connect uplinkHost={plan.UplinkHost} uplinkPort={Selected.UplinkPort}");

            _watchdog = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ = WatchdogAsync(_watchdog.Token);

            await _engine.StartAsync(configJson, token);
            var budget = ConnectTimeouts.ConnectMs;
            var probe = await Task.Run(() => VpnTunnelProbe.WaitForTunnelReady(budget), token);
            if (probe != TunnelReadyResult.Ok)
            {
                var reason = probe == TunnelReadyResult.TcpFail ? "diag proxy unreachable" : "upstream http fail";
                throw new InvalidOperationException($"Tunnel probe failed: {reason}");
            }
            SetState(VpnState.Connected);
            AppLog.Append("[vpn] connected");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _engine.StopAsync();
            var msg = ex.Message;
            SetState(VpnState.Error(msg));
            ErrorRaised?.Invoke(msg);
            AppServices.Snackbar.ShowError(msg);
            AppLog.Append($"[vpn] connect error: {msg}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (State.Kind is VpnStateKind.Disconnected or VpnStateKind.Disconnecting) return;
        _op?.Cancel();
        _watchdog?.Cancel();
        SetState(VpnState.Disconnecting);
        AppLog.Append("[ui] windows home: disconnect");
        try
        {
            await _engine.StopAsync();
            Throughput = new(0, 0);
            Raise(nameof(Throughput));
            SetState(VpnState.Disconnected);
            AppLog.Append("[vpn] disconnected");
        }
        catch (Exception ex)
        {
            AppLog.Append($"[vpn] disconnect error: {ex.Message}");
            SetState(VpnState.Disconnected);
        }
    }

    public void TickThroughput()
    {
        if (State.Kind != VpnStateKind.Connected)
        {
            if (Throughput.DownloadBps != 0 || Throughput.UploadBps != 0)
            {
                Throughput = new(0, 0);
                Raise(nameof(Throughput));
            }
            return;
        }
        // Real stats via clash API — roadmap; show zeros instead of mock random.
        if (Throughput.DownloadBps != 0 || Throughput.UploadBps != 0)
        {
            Throughput = new(0, 0);
            Raise(nameof(Throughput));
        }
    }

    private async Task WatchdogAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ConnectTimeouts.ConnectMs, token);
            if (State.Kind == VpnStateKind.Connecting)
            {
                await _engine.StopAsync();
                SetState(VpnState.Error("Connect timeout"));
                ErrorRaised?.Invoke("Connect timeout");
                AppServices.Snackbar.ShowError("Connect timeout");
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyEvent(VpnStateEvent evt)
    {
        var mapped = evt.State switch
        {
            "connecting" => VpnState.Connecting,
            "connected" => VpnState.Connected,
            "disconnecting" => VpnState.Disconnecting,
            "disconnected" => VpnState.Disconnected,
            "error" => VpnState.Error(evt.Message),
            _ => (VpnState?)null,
        };
        if (mapped is not null) SetState(mapped);
    }

    private void SetState(VpnState state)
    {
        State = state;
        Raise(nameof(State));
        if (state.Kind is VpnStateKind.Connected or VpnStateKind.Disconnected or VpnStateKind.Error)
            _watchdog?.Cancel();
    }
}
