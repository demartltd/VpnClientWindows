using DemarVpn.Domain.Vpn;
using DemarVpn.Models;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Network;
using DemarVpn.Services.Servers;

namespace DemarVpn.Services.Vpn;

public sealed class VpnController : NotifyBase
{
    private readonly IVpnEngine _engine = VpnEngineFactory.Create();
    private CancellationTokenSource? _op;
    private CancellationTokenSource? _watchdog;
    private CancellationTokenSource? _health;
    private List<ServerProfile> _servers = [];
    private Dictionary<string, ServerReachState> _reachability = new();
    private readonly object _reachLock = new();
    private CancellationTokenSource? _reachCts;
    private static readonly string LastProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "last_profile_id");

    public VpnState State { get; private set; } = VpnState.Disconnected;
    public ServerProfile? Selected { get; private set; }
    public ThroughputRates Throughput { get; private set; } = new(0, 0);
    public IReadOnlyList<ServerProfile> Servers => _servers;
    public IReadOnlyDictionary<string, ServerReachState> ServerReachability
    {
        get { lock (_reachLock) return new Dictionary<string, ServerReachState>(_reachability); }
    }
    public bool ServersLoading { get; private set; }
    public string? ServersError { get; private set; }

    public event Action<string>? ErrorRaised;

    public void ClearServers()
    {
        _reachCts?.Cancel();
        _servers = [];
        _reachability = new();
        Selected = null;
        ServersError = null;
        Raise(nameof(Servers));
        Raise(nameof(Selected));
        Raise(nameof(ServersError));
        Raise(nameof(ServerReachability));
    }

    public async Task OnSignedOutAsync()
    {
        if (State.Kind is not VpnStateKind.Disconnected)
            await DisconnectAsync();
        ClearLastProfileId();
        ClearServers();
    }

    public async Task RefreshServersAsync(CancellationToken ct = default)
    {
        var session = AuthSessionStore.Current;
        if (session is null)
        {
            ClearServers();
            return;
        }

        ServersLoading = true;
        ServersError = null;
        Raise(nameof(ServersLoading));
        Raise(nameof(ServersError));
        try
        {
            var result = await ServersApi.FetchServersAsync(session.UserId, session.LoginAt, ct);
            ApplyServerList(result.Servers);
            AppLog.Append($"servers: loaded {result.Servers.Count} (sub={result.VpnSubscription})");
            await RefreshServerReachabilityAsync(ct);
        }
        catch (AuthException e) when (e.StatusCode == 401)
        {
            AuthRepository.ClearLocal();
            AppServices.Auth.NotifyChanged();
            await OnSignedOutAsync();
            AppLog.Append("servers: session revoked");
        }
        catch (Exception e)
        {
            ServersError = e.Message;
            Raise(nameof(ServersError));
            AppLog.Append($"servers: fetch failed — {e.Message}");
        }
        finally
        {
            ServersLoading = false;
            Raise(nameof(ServersLoading));
        }
    }

    public async Task RefreshServerReachabilityAsync(CancellationToken ct = default)
    {
        if (_servers.Count == 0) return;
        _reachCts?.Cancel();
        _reachCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _reachCts.Token;
        lock (_reachLock)
            _reachability = _servers.ToDictionary(s => s.Id, _ => new ServerReachState(ServerReachStatus.Checking));
        Raise(nameof(ServerReachability));

        await Task.WhenAll(_servers.Select(async profile =>
        {
            var state = await ServerReachProbe.ProbeAsync(profile);
            if (token.IsCancellationRequested) return;
            lock (_reachLock)
                _reachability[profile.Id] = state;
            Raise(nameof(ServerReachability));
        }));
    }

    public void Select(ServerProfile profile)
    {
        if (profile.Id == Selected?.Id) return;
        Selected = profile;
        SaveLastProfileId(profile.Id);
        Raise(nameof(Selected));
        AppLog.Append($"[ui] server selected: {profile.DisplayName}");
        if (State.Kind is VpnStateKind.Connected or VpnStateKind.Connecting or VpnStateKind.Error)
            _ = SwitchServerAsync();
    }

    public async Task ConnectAsync()
    {
        if (State.IsBusy || State.Kind == VpnStateKind.Connected) return;
        if (Selected is null)
        {
            AppServices.Snackbar.ShowError(Strings.ServerNotSelected);
            return;
        }

        _op?.Cancel();
        _watchdog?.Cancel();
        _health?.Cancel();
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
            if (resolved.Count == 0 && HostUtils.IsHostname(plan.UplinkHost))
            {
                await Task.Delay(200, ct);
                resolved = await UplinkIpv4.ResolveAsync(plan.UplinkHost);
            }
            if (resolved.Count > 0) plan = plan.WithExtraDirectIps(resolved);

            var connectTarget = resolved.Count > 0
                ? resolved[0]
                : HostUtils.IsLiteralIpv4(plan.UplinkHost) ? plan.UplinkHost : null;
            if (connectTarget is null)
            {
                AppLog.Append($"[vpn] uplink resolve empty for {plan.UplinkHost}");
                throw new InvalidOperationException(Strings.VpnServerUnreachable);
            }

            var port = Selected.UplinkPort;
            var configJson = SingBoxConfigBuilder.BuildTunConfigJson(plan);
            AppLog.Append($"connect uplinkHost={plan.UplinkHost} uplinkPort={port} target={connectTarget} resolved={string.Join(",", resolved)}");

            _watchdog = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ = WatchdogAsync(_watchdog.Token);

            await _engine.StartAsync(configJson, ct);
            var probe = await VpnTunnelProbe.WaitForTunnelReadyAsync(ConnectTimeouts.ProbeMs, ct);
            if (probe != TunnelReadyResult.Ok)
            {
                var reason = probe switch
                {
                    TunnelReadyResult.TcpFail => "diag proxy unreachable",
                    TunnelReadyResult.ProxyFail => "proxy path fail",
                    TunnelReadyResult.TunFail => "tun/dns path fail",
                    _ => "upstream fail",
                };
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
        _health?.Cancel();
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

    public async Task HardRestartCoreAsync()
    {
        if (State.Kind != VpnStateKind.Connected)
            throw new InvalidOperationException(Strings.ServiceRestartFail);
        await DisconnectAsync();
        await ConnectAsync();
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

    private void ApplyServerList(IReadOnlyList<ServerProfile> servers)
    {
        _servers = servers.ToList();
        var lastId = LoadLastProfileId();
        Selected = _servers.FirstOrDefault(s => s.Id == Selected?.Id)
                   ?? _servers.FirstOrDefault(s => s.Id == lastId)
                   ?? _servers.FirstOrDefault();
        if (Selected is not null) SaveLastProfileId(Selected.Id);
        Raise(nameof(Servers));
        Raise(nameof(Selected));
        Raise(nameof(ServersError));
    }

    private async Task SwitchServerAsync()
    {
        _op?.Cancel();
        _watchdog?.Cancel();
        _health?.Cancel();
        var kind = State.Kind;
        if (kind == VpnStateKind.Connected)
            await DisconnectAsync();
        else if (kind is VpnStateKind.Connecting or VpnStateKind.Error)
        {
            await _engine.StopAsync();
            SetState(VpnState.Disconnected);
        }
        await Task.Delay(ConnectTimeouts.SwitchSettleMs);
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

    private async Task HealthLoopAsync(CancellationToken token)
    {
        var fails = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(ConnectTimeouts.HealthIntervalMs, token);
                if (State.Kind != VpnStateKind.Connected) return;

                var ok = _engine.IsRunning && await TunnelPathProbe.TraceOkAsync(
                    TunnelPathProbe.TunClient, TunnelPathProbe.TraceHostUrl, token);
                if (ok)
                {
                    fails = 0;
                    continue;
                }

                if (++fails < ConnectTimeouts.HealthFailLimit) continue;

                _health?.Cancel();
                await _engine.StopAsync();
                var msg = Strings.VpnTunnelLost;
                SetState(VpnState.Error(msg));
                ErrorRaised?.Invoke(msg);
                AppServices.Snackbar.ShowError(msg);
                AppLog.Append($"[vpn] health fail: {msg}");
                return;
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

        if (state.Kind == VpnStateKind.Connected)
        {
            _health?.Cancel();
            _health = new CancellationTokenSource();
            _ = HealthLoopAsync(_health.Token);
        }
        else
        {
            _health?.Cancel();
        }
    }

    private static string? LoadLastProfileId()
    {
        try
        {
            return File.Exists(LastProfilePath) ? File.ReadAllText(LastProfilePath).Trim() : null;
        }
        catch { return null; }
    }

    private static void SaveLastProfileId(string id)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastProfilePath)!);
            File.WriteAllText(LastProfilePath, id);
        }
        catch { /* ignore */ }
    }

    private static void ClearLastProfileId()
    {
        try { if (File.Exists(LastProfilePath)) File.Delete(LastProfilePath); }
        catch { /* ignore */ }
    }
}
