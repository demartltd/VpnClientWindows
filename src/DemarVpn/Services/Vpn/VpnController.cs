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

    public VpnState State { get; private set; } = VpnState.Disconnected;
    public ServerProfile Selected { get; private set; } = TestServer.Default;
    public ThroughputRates Throughput { get; private set; } = new(0, 0);
    public IReadOnlyList<ServerProfile> Servers => TestServer.All;

    public event Action<string>? ErrorRaised;

    public void Select(ServerProfile profile)
    {
        if (profile.Id == Selected.Id) return;
        Selected = profile;
        Raise(nameof(Selected));
        AppLog.Append($"[ui] server selected: {profile.DisplayName}");
        if (State.Kind is VpnStateKind.Connected or VpnStateKind.Connecting or VpnStateKind.Error)
            _ = SwitchServerAsync();
    }

    public async Task ConnectAsync()
    {
        if (State.IsBusy || State.Kind == VpnStateKind.Connected) return;
        _op?.Cancel();
        _watchdog?.Cancel();
        _op = new CancellationTokenSource();
        var token = _op.Token;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ConnectTimeouts.ConnectMs);
        var ct = deadline.Token;
        SetState(VpnState.Connecting);
        AppLog.Append("[ui] windows home: connect");
        try
        {
            var plan = Selected.ConnectPlan;
            var resolved = await UplinkIpv4.ResolveAsync(plan.UplinkHost);
            if (resolved.Count > 0) plan = plan.WithExtraDirectIps(resolved);

            var port = Selected.UplinkPort ?? 443;
            var target = await UplinkProbe.FirstReachableAsync(plan, resolved, port, ct);
            if (target is null)
            {
                AppLog.Append($"[vpn] uplink preflight failed for {plan.UplinkHost}:{port}");
                throw new InvalidOperationException(Strings.VpnServerUnreachable);
            }

            var configJson = SingBoxConfigBuilder.BuildTunConfigJson(plan);
            AppLog.Append($"connect uplinkHost={plan.UplinkHost} uplinkPort={port} target={target} resolved={string.Join(",", resolved)}");

            _watchdog = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ = WatchdogAsync(_watchdog.Token);

            await _engine.StartAsync(configJson, ct);
            var probe = await VpnTunnelProbe.WaitForTunnelReadyAsync(ConnectTimeouts.ProbeMs, ct);
            if (probe != TunnelReadyResult.Ok)
            {
                var reason = probe == TunnelReadyResult.TcpFail ? "diag proxy unreachable" : "upstream http fail";
                throw new InvalidOperationException($"{Strings.VpnConnectFailed}: {reason}");
            }
            SetState(VpnState.Connected);
            AppLog.Append("[vpn] connected");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (State.Kind == VpnStateKind.Connecting)
                SetState(VpnState.Disconnected);
        }
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
        if (Throughput.DownloadBps != 0 || Throughput.UploadBps != 0)
        {
            Throughput = new(0, 0);
            Raise(nameof(Throughput));
        }
    }

    private async Task SwitchServerAsync()
    {
        _op?.Cancel();
        _watchdog?.Cancel();
        var kind = State.Kind;
        if (kind == VpnStateKind.Connected)
            await DisconnectAsync();
        else if (kind is VpnStateKind.Connecting or VpnStateKind.Error)
        {
            await _engine.StopAsync();
            SetState(VpnState.Disconnected);
        }
        await ConnectAsync();
    }

    private async Task WatchdogAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ConnectTimeouts.WatchdogMs, token);
            if (State.Kind == VpnStateKind.Connecting)
            {
                _op?.Cancel();
                await _engine.StopAsync();
                SetState(VpnState.Error(Strings.VpnConnectFailed));
                ErrorRaised?.Invoke(Strings.VpnConnectFailed);
                AppServices.Snackbar.ShowError(Strings.VpnConnectFailed);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SetState(VpnState state)
    {
        State = state;
        Raise(nameof(State));
        if (state.Kind is VpnStateKind.Connected or VpnStateKind.Disconnected or VpnStateKind.Error)
            _watchdog?.Cancel();
    }
}
