using DemarVpn.Domain.Vpn;
using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Models;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Network;
using DemarVpn.Services.Servers;
using DemarVpn.Services.Support;

namespace DemarVpn.Services.Vpn;

public sealed class VpnController : NotifyBase
{
    private readonly IVpnEngine _engine = new SingBoxProcessEngine();
    private readonly SemaphoreSlim _ops = new(1, 1);
    private int _ensureStopGate;
    private int _switchPending;
    private CancellationTokenSource? _op;
    private CancellationTokenSource? _watchdog;
    private CancellationTokenSource? _health;
    private List<ServerProfile> _servers = [];
    private Dictionary<string, ServerReachState> _reachability = new();
    private readonly object _reachLock = new();
    private CancellationTokenSource? _reachCts;
    private CancellationTokenSource? _trafficCts;
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
    public bool TrafficExceeded { get; private set; }

    public event Action<string>? ErrorRaised;
    public event Action? TrafficLimitReached;

    public VpnController() => _engine.CoreExited += OnCoreExited;

    public static bool IsFreeSubscription() =>
        TunSubscription.IsFree(
            AccountStore.Account?.VpnSubscription,
            AuthSessionStore.Current?.VpnSubscription);

    public async Task RefreshTunGroupsAsync(CancellationToken ct = default)
    {
        try
        {
            var payload = await TunGroupsApi.FetchAsync(ct);
            if (payload is null) return;
            TunSettingsStore.CacheGroups(payload.Catalog, payload.Raw);
            AppLog.Append($"tun_groups: loaded {payload.Catalog.AppGroups.Count} app + {payload.Catalog.SiteGroups.Count} site");
        }
        catch (Exception ex)
        {
            AppLog.Append($"tun_groups: fetch failed — {ex.Message}");
        }
    }

    private async Task EnsureConnectPrereqsAsync(CancellationToken ct)
    {
        if (AuthRepository.IsSignedIn && AccountStore.Account is null)
            await AccountStore.RefreshAsync(ct);
        if (TunSettingsStore.Catalog.AppGroups.Count == 0 && TunSettingsStore.Catalog.SiteGroups.Count == 0)
            await RefreshTunGroupsAsync(ct);
    }

    public void AddBypassExe(string exe)
    {
        if (IsFreeSubscription()) return;
        TunSettingsStore.AddBypassExe(exe);
        AppLog.Append($"tun_apps=add {TunExeNames.Normalize(exe)}");
    }

    public void RemoveBypassExe(string exe)
    {
        TunSettingsStore.RemoveBypassExe(exe);
        AppLog.Append($"tun_apps=remove {TunExeNames.Normalize(exe)}");
    }

    public void SetAppGroupEnabled(string groupId, bool enabled)
    {
        if (IsFreeSubscription()) return;
        TunSettingsStore.SetAppGroupEnabled(groupId, enabled);
        AppLog.Append($"tun_groups={(enabled ? "on" : "off")} {groupId}");
    }

    public void SetSiteGroupEnabled(string groupId, bool enabled)
    {
        if (IsFreeSubscription()) return;
        TunSettingsStore.SetSiteGroupEnabled(groupId, enabled);
        AppLog.Append($"tun_sites={(enabled ? "on" : "off")} {groupId}");
    }

    public void SetTunRulesEnabled(bool enabled)
    {
        if (IsFreeSubscription()) return;
        TunSettingsStore.SetRulesEnabled(enabled);
        AppLog.Append($"tun_rules={(enabled ? "on" : "off")}");
    }

    private string TunSettingsFingerprint() => TunSettingsStore.SettingsFingerprint(IsFreeSubscription());

    /// <summary>Open account traffic sheet (connect block + demarvpn://settings/traffic).</summary>
    public void RaiseTrafficLimitReached() => TrafficLimitReached?.Invoke();

    public void ClearServers()
    {
        _reachCts?.Cancel();
        _servers = [];
        _reachability = new();
        Selected = null;
        ServersError = null;
        TrafficExceeded = false;
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
            AccountStore.Clear();
            SupportStore.ClearAll();
            return;
        }

        ServersLoading = true;
        ServersError = null;
        Raise(nameof(ServersLoading));
        Raise(nameof(ServersError));
        try
        {
            var serversTask = ServersApi.FetchServersAsync(session.UserId, session.LoginAt, ct);
            var accountTask = AccountStore.RefreshAsync(ct);
            var tunTask = RefreshTunGroupsAsync(ct);
            await Task.WhenAll(serversTask, accountTask, tunTask);
            var result = await serversTask;
            ApplyServerList(result.Servers);
            TrafficExceeded = result.TrafficExceeded;
            Raise(nameof(TrafficExceeded));
            AppLog.Append($"servers: loaded {result.Servers.Count} (sub={result.VpnSubscription})");
            _ = SupportStore.RefreshUnreadAsync(ct);
            await RefreshServerReachabilityAsync(ct);
        }
        catch (AuthException e) when (e.IsSessionInvalid)
        {
            AuthRepository.ClearLocal();
            AppServices.Auth.NotifyChanged();
            AccountStore.Clear();
            SupportStore.ClearAll();
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
        _ = UplinkIpv4.ResolveAsync(profile.Profile.Server);
        if (State.Kind is not (VpnStateKind.Connected or VpnStateKind.Connecting or VpnStateKind.Error))
            return;
        Interlocked.Exchange(ref _switchPending, 1);
        CancelOps();
        _ = RunExclusiveAsync(DrainSwitchAsync);
    }

    public Task ConnectAsync() => RunExclusiveAsync(ConnectCoreAsync);

    public async Task EnsureStoppedAsync()
    {
        if (Interlocked.CompareExchange(ref _ensureStopGate, 1, 0) != 0) return;
        try
        {
            CancelOps();
            if (State.Kind is VpnStateKind.Disconnected) return;

            try { await _engine.StopAsync(); }
            catch (Exception ex) { AppLog.Append($"[vpn] ensure stop: {ex.Message}"); }

            ClearThroughput();
            SetState(VpnState.Disconnected);
            AppLog.Append("[vpn] stopped on app exit");
        }
        finally
        {
            Interlocked.Exchange(ref _ensureStopGate, 0);
        }
    }

    public void EnsureStoppedSync() => EnsureStoppedAsync().GetAwaiter().GetResult();

    public Task DisconnectAsync() => RunExclusiveAsync(DisconnectCoreAsync);

    public Task HardRestartCoreAsync() => RunExclusiveAsync(async () =>
    {
        if (State.Kind != VpnStateKind.Connected)
            throw new InvalidOperationException(Strings.ServiceRestartFail);
        await StopToDisconnectedAsync();
        await ConnectCoreAsync();
    });

    private void ApplyServerList(IReadOnlyList<ServerProfile> servers)
    {
        _servers = servers.ToList();
        var lastId = LoadLastProfileId();
        Selected = _servers.FirstOrDefault(s => s.Id == Selected?.Id)
                   ?? _servers.FirstOrDefault(s => s.Id == lastId)
                   ?? _servers.FirstOrDefault();
        if (Selected is not null)
        {
            SaveLastProfileId(Selected.Id);
            _ = UplinkIpv4.ResolveAsync(Selected.Profile.Server);
        }
        Raise(nameof(Servers));
        Raise(nameof(Selected));
        Raise(nameof(ServersError));
    }

    private async Task RunExclusiveAsync(Func<Task> action)
    {
        await _ops.WaitAsync();
        try { await action(); }
        finally { _ops.Release(); }
    }

    private async Task DrainSwitchAsync()
    {
        while (Volatile.Read(ref _switchPending) == 1)
        {
            Interlocked.Exchange(ref _switchPending, 0);
            await StopToDisconnectedAsync();
            await Task.Delay(ConnectTimeouts.SwitchSettleMs);
            if (Volatile.Read(ref _switchPending) == 1)
                continue;
            await ConnectCoreAsync();
        }
    }

    private async Task ConnectCoreAsync()
    {
        if (State.Kind == VpnStateKind.Connected) return;
        if (State.IsBusy) await StopToDisconnectedAsync();

        var profile = Selected;
        if (profile is null)
        {
            AppServices.Snackbar.ShowError(Strings.ServerNotSelected);
            return;
        }
        if (TrafficExceeded)
        {
            AppLog.Append("[vpn] connect blocked: traffic limit");
            RaiseTrafficLimitReached();
            return;
        }

        CancelOps();
        ClearThroughput();
        _op = new CancellationTokenSource();
        var token = _op.Token;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ConnectTimeouts.ConnectMs);
        var ct = deadline.Token;
        SetState(VpnState.Connecting);
        AppLog.Append("[ui] windows home: connect");
        try
        {
            var plan = profile.ConnectPlan;
            var (resolved, dnsErr) = await UplinkIpv4.ResolveDetailedAsync(plan.UplinkHost);
            if (resolved.Count > 0) plan = plan.WithExtraDirectIps(resolved);

            var connectTarget = resolved.Count > 0
                ? resolved[0]
                : HostUtils.IsLiteralIpv4(plan.UplinkHost) ? plan.UplinkHost : null;
            if (connectTarget is null)
            {
                var why = dnsErr ?? "empty";
                AppLog.Append($"[vpn] uplink resolve empty for {plan.UplinkHost}: {why}");
                throw new InvalidOperationException($"{Strings.VpnServerUnreachable}: {plan.UplinkHost} ({why})");
            }

            await EnsureConnectPrereqsAsync(ct);
            var bypass = TunSettingsStore.EffectiveBypass(IsFreeSubscription());
            var configJson = SingBoxConfigBuilder.BuildTunConfigJson(plan, bypass);
            AppLog.Append(
                $"connect uplinkHost={plan.UplinkHost} uplinkPort={profile.UplinkPort} target={connectTarget} resolved={string.Join(",", resolved)} " +
                $"tun={TunSettingsFingerprint()} exes={bypass.BypassExes.Count} hosts={bypass.BypassHosts.Count} rs={plan.BypassRuleSets.Count}");

            _watchdog = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ = WatchdogAsync(_watchdog.Token);

            await _engine.StartAsync(configJson, ct);

            var probe = await TunnelPathProbe.Tcp443ViaDiagAsync("1.1.1.1", ct);
            if (!probe.Ok)
            {
                AppLog.Append($"[vpn] tunnel path probe failed: {probe.Detail}");
                throw new InvalidOperationException($"{Strings.VpnConnectFailed}: {probe.Detail}");
            }

            AppLog.Append($"[vpn] tunnel path ok {probe.ElapsedMs}ms");
            SetState(VpnState.Connected);
            AppLog.Append("[vpn] connected");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            try { await _engine.StopAsync(); }
            catch (Exception stopEx) { AppLog.Append($"[vpn] stop after cancel: {stopEx.Message}"); }
            if (State.Kind == VpnStateKind.Connecting)
                SetState(VpnState.Disconnected);
        }
        catch (Exception ex)
        {
            try { await _engine.StopAsync(); }
            catch (Exception stopEx) { AppLog.Append($"[vpn] stop after fail: {stopEx.Message}"); }
            ClearThroughput();
            var msg = ex.Message;
            SetState(VpnState.Error(msg));
            ErrorRaised?.Invoke(msg);
            AppServices.Snackbar.ShowError(msg);
            AppLog.Append($"[vpn] connect error: {msg}");
            if (ex.InnerException is not null || ex is not InvalidOperationException)
                AppLog.Append($"[vpn] connect error detail: {ex}");
        }
    }

    private async Task DisconnectCoreAsync()
    {
        if (State.Kind is VpnStateKind.Disconnected or VpnStateKind.Disconnecting) return;
        Interlocked.Exchange(ref _switchPending, 0);
        AppLog.Append("[ui] windows home: disconnect");
        await StopToDisconnectedAsync();
        AppLog.Append("[vpn] disconnected");
    }

    private async Task StopToDisconnectedAsync()
    {
        CancelOps();
        if (State.Kind is VpnStateKind.Disconnected) return;
        SetState(VpnState.Disconnecting);
        try { await _engine.StopAsync(); }
        catch (Exception ex) { AppLog.Append($"[vpn] stop: {ex.Message}"); }
        ClearThroughput();
        SetState(VpnState.Disconnected);
    }

    private void CancelOps()
    {
        _op?.Cancel();
        _watchdog?.Cancel();
        _health?.Cancel();
        StopTraffic();
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

                var ok = await UplinkProbe.TcpReachableAsync(
                    SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, 500, token);
                if (ok)
                {
                    fails = 0;
                    continue;
                }

                if (++fails < ConnectTimeouts.HealthFailLimit) continue;

                _health?.Cancel();
                StopTraffic();
                await _engine.StopAsync();
                ClearThroughput();
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

    private void OnCoreExited()
    {
        if (State.Kind is VpnStateKind.Disconnected or VpnStateKind.Disconnecting or VpnStateKind.Connecting)
            return;
        CancelOps();
        ClearThroughput();
        SetState(VpnState.Disconnected);
        AppLog.Append("[vpn] core exited");
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
            StartTraffic();
        }
        else
        {
            _health?.Cancel();
            StopTraffic();
            ClearThroughput();
        }
    }

    private void StartTraffic()
    {
        StopTraffic();
        _trafficCts = new CancellationTokenSource();
        var ct = _trafficCts.Token;
        _ = ClashTrafficMonitor.RunAsync((up, down) =>
        {
            Throughput = new(down, up);
            Raise(nameof(Throughput));
        }, ct);
    }

    private void StopTraffic()
    {
        _trafficCts?.Cancel();
        _trafficCts = null;
    }

    private void ClearThroughput()
    {
        if (Throughput.DownloadBps == 0 && Throughput.UploadBps == 0) return;
        Throughput = new(0, 0);
        Raise(nameof(Throughput));
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
