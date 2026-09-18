using DemarVpn.Controls;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Views;

public sealed partial class ServersPage : Page
{
    private ServerProtocol _protocol = ServerProtocol.Hysteria2;
    private ServerTier? _tierFilter;
    private bool _wired;
    private int _tierBarServersHash;
    private bool _refreshing;
    private Storyboard? _spin;

    public event Action? NeedUpgrade;

    public ServersPage()
    {
        InitializeComponent();
        ProtocolBar.SelectionChanged += (_, id) =>
        {
            if (!TryParseProtocol(id, out var next) || next == _protocol) return;
            _protocol = next;
            RebuildList();
        };
        TierBar.SelectionChanged += (_, id) =>
        {
            var next = id == "ALL" ? null : ParseTier(id);
            if (next == _tierFilter) return;
            if (next is { } tier)
            {
                var sub = AccountStore.Account?.VpnSubscription ?? AuthSessionStore.Current?.VpnSubscription;
                if (tier.RequiresUpgrade(sub))
                {
                    NeedUpgrade?.Invoke();
                    TierBar.SelectedId = _tierFilter?.Label() ?? "ALL";
                    return;
                }
            }
            _tierFilter = next;
            RebuildList();
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_wired)
        {
            _wired = true;
            RefreshShadow.Receivers.Add(ShadowCast);
        }
        AppServices.Vpn.PropertyChanged -= OnVpnChanged;
        AppServices.Vpn.PropertyChanged += OnVpnChanged;
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        AppServices.Vpn.PropertyChanged -= OnVpnChanged;

    private void OnVpnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Services.Vpn.VpnController.Selected))
            DispatcherQueue.TryEnqueue(SyncSelection);
        else if (e.PropertyName == nameof(Services.Vpn.VpnController.ServerReachability))
            DispatcherQueue.TryEnqueue(SyncReachability);
        else if (e.PropertyName is nameof(Services.Vpn.VpnController.Servers)
            or nameof(Services.Vpn.VpnController.ServersLoading)
            or nameof(Services.Vpn.VpnController.ServersError))
            DispatcherQueue.TryEnqueue(Rebuild);
    }

    public void OnTabVisible()
    {
        if (!AppServices.Auth.SignedIn) return;
        var vpn = AppServices.Vpn;
        if (vpn.Servers.Count == 0 && !vpn.ServersLoading)
            _ = vpn.RefreshServersAsync();
        else if (vpn.Servers.Count > 0 && vpn.ServerReachability.Count == 0)
            _ = vpn.RefreshServerReachabilityAsync();
        Rebuild();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_refreshing || !AppServices.Auth.SignedIn) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        StartSpin();
        try
        {
            await AppServices.Vpn.RefreshServersAsync();
            if (AppServices.Vpn.ServersError is { } err)
                AppServices.Snackbar.ShowError(string.IsNullOrWhiteSpace(err) ? Strings.ServersRefreshFail : err);
            else
                AppServices.Snackbar.ShowSuccess(Strings.ServersRefreshOk);
        }
        catch (Exception ex)
        {
            AppServices.Snackbar.ShowError(
                string.IsNullOrWhiteSpace(ex.Message) ? Strings.ServersRefreshFail : ex.Message);
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StopSpin();
                _refreshing = false;
                RefreshButton.IsEnabled = true;
            });
        }
    }

    private void StartSpin()
    {
        _spin ??= CreateSpin();
        _spin.Begin();
    }

    private void StopSpin()
    {
        _spin?.Stop();
        RefreshIconRotate.Angle = 0;
    }

    private Storyboard CreateSpin()
    {
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(700),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(anim, RefreshIconRotate);
        Storyboard.SetTargetProperty(anim, "Angle");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        return sb;
    }

    private void RebuildFilters()
    {
        ProtocolBar.SetOptions(
            Enum.GetValues<ServerProtocol>()
                .Select(p => new SegmentOption(p.ToString().ToUpperInvariant(), p.Label()))
                .ToList(),
            _protocol.ToString().ToUpperInvariant());

        var servers = AppServices.Vpn.Servers;
        var hash = servers.Count;
        foreach (var s in servers) hash = HashCode.Combine(hash, s.Id, s.Tier);
        if (hash == _tierBarServersHash && TierBar.SelectedId.Length > 0) return;
        _tierBarServersHash = hash;

        var opts = new List<SegmentOption> { new("ALL", "All") };
        foreach (var t in ServerProfile.VisibleTiers(servers))
            opts.Add(new(t.Label(), TitleCase(t.Label())));
        var selected = _tierFilter?.Label() ?? "ALL";
        if (opts.All(o => o.Id != selected))
        {
            _tierFilter = null;
            selected = "ALL";
        }
        TierBar.SetOptions(opts, selected);
    }

    private void Rebuild()
    {
        RebuildFilters();
        RefreshFab.Visibility = AppServices.Auth.SignedIn ? Visibility.Visible : Visibility.Collapsed;
        RebuildList();
    }

    private void RebuildList()
    {
        ListHost.Children.Clear();
        if (!AppServices.Auth.SignedIn) return;

        var vpn = AppServices.Vpn;
        if (vpn.ServersLoading && vpn.Servers.Count == 0)
        {
            ListHost.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 36,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        if (vpn.ServersError is { } err && vpn.Servers.Count == 0)
        {
            ListHost.Children.Add(new TextBlock
            {
                Text = err,
                Foreground = (Brush)Application.Current.Resources["DemarErrorBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
            var retry = new Button { Content = Strings.ServersRetry, HorizontalAlignment = HorizontalAlignment.Stretch };
            retry.Click += async (_, _) => await vpn.RefreshServersAsync();
            ListHost.Children.Add(retry);
            return;
        }

        var list = ServerProfile.Filter(vpn.Servers, _protocol, _tierFilter);
        if (list.Count == 0)
        {
            ListHost.Children.Add(new TextBlock { Text = Strings.ServersEmpty, Opacity = 0.7 });
            return;
        }

        var selected = vpn.Selected;
        var reachMap = vpn.ServerReachability;
        foreach (var profile in list)
        {
            reachMap.TryGetValue(profile.Id, out var reach);
            var row = new ServerRow
            {
                Profile = profile,
                IsSelected = profile.Id == selected?.Id,
                ReachState = reach ?? new ServerReachState(ServerReachStatus.Checking),
            };
            row.Click += (_, _) =>
            {
                vpn.Select(profile);
                AppServices.Snackbar.ShowSuccess(string.Format(Strings.ServerSelected, profile.ListTitle));
            };
            ListHost.Children.Add(row);
        }
    }

    private void SyncSelection()
    {
        var id = AppServices.Vpn.Selected?.Id;
        foreach (var child in ListHost.Children)
            if (child is ServerRow row)
                row.IsSelected = row.Profile?.Id == id;
    }

    private void SyncReachability()
    {
        var map = AppServices.Vpn.ServerReachability;
        foreach (var child in ListHost.Children)
        {
            if (child is not ServerRow row || row.Profile is not { } p) continue;
            map.TryGetValue(p.Id, out var reach);
            row.ReachState = reach ?? new ServerReachState(ServerReachStatus.Checking);
        }
    }

    private static bool TryParseProtocol(string id, out ServerProtocol protocol)
    {
        foreach (var p in Enum.GetValues<ServerProtocol>())
        {
            if (p.ToString().Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                protocol = p;
                return true;
            }
        }
        protocol = default;
        return false;
    }

    private static ServerTier? ParseTier(string id)
    {
        foreach (var t in Enum.GetValues<ServerTier>())
            if (t.Label() == id) return t;
        return null;
    }

    private static string TitleCase(string label) =>
        label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
}
