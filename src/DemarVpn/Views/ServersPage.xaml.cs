using DemarVpn.Controls;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services;
using DemarVpn.Services.Servers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Views;

public sealed partial class ServersPage : Page
{
    private bool _wired;
    private bool _refreshing;
    private bool _listDirty;
    private Storyboard? _spin;

    public event Action? NeedUpgrade;
    public event Action? FiltersRequested;
    public event Action<ServerProfile>? DetailsRequested;

    public ServersPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_wired)
        {
            _wired = true;
            RefreshShadow.Receivers.Add(ShadowCast);
            FilterShadow.Receivers.Add(ShadowCast);
        }
        AppServices.Vpn.PropertyChanged -= OnVpnChanged;
        AppServices.Vpn.PropertyChanged += OnVpnChanged;
        ServersPreferencesStore.Changed -= OnFiltersChanged;
        ServersPreferencesStore.Changed += OnFiltersChanged;
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Vpn.PropertyChanged -= OnVpnChanged;
        ServersPreferencesStore.Changed -= OnFiltersChanged;
    }

    private void OnFiltersChanged() =>
        Ui.Run(DispatcherQueue, () =>
        {
            if (IsTabVisible()) Rebuild();
            else _listDirty = true;
        });

    private void OnVpnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Services.Vpn.VpnController.Selected))
            Ui.Run(DispatcherQueue, () => { if (IsTabVisible()) SyncSelection(); });
        else if (e.PropertyName == nameof(Services.Vpn.VpnController.ServerReachability))
            Ui.Run(DispatcherQueue, () =>
            {
                if (IsTabVisible()) SyncReachability();
                else _listDirty = true;
            });
        else if (e.PropertyName is nameof(Services.Vpn.VpnController.Servers)
            or nameof(Services.Vpn.VpnController.ServersLoading)
            or nameof(Services.Vpn.VpnController.ServersError))
            Ui.Run(DispatcherQueue, () =>
            {
                if (IsTabVisible()) Rebuild();
                else _listDirty = true;
            });
    }

    private bool IsTabVisible() => Visibility == Visibility.Visible;

    public void OnTabVisible()
    {
        if (!AppServices.Auth.SignedIn) return;
        var vpn = AppServices.Vpn;
        if (vpn.Servers.Count == 0 && !vpn.ServersLoading)
            _ = vpn.RefreshServersAsync();
        else if (vpn.Servers.Count > 0 && vpn.ServerReachability.Count == 0)
            _ = vpn.RefreshServerReachabilityAsync();
        if (_listDirty || ListHost.Children.Count == 0)
        {
            _listDirty = false;
            Rebuild();
        }
    }

    private void OnFilterClick(object sender, RoutedEventArgs e) => FiltersRequested?.Invoke();

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_refreshing || !AppServices.Auth.SignedIn) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        StartSpin();
        try
        {
            await Ui.ApiAsync(DispatcherQueue, () => AppServices.Vpn.RefreshServersAsync());
            var err = AppServices.Vpn.ServersError;
            if (err is not null)
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
            StopSpin();
            _refreshing = false;
            RefreshButton.IsEnabled = true;
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

    private void Rebuild()
    {
        var signedIn = AppServices.Auth.SignedIn;
        FilterFab.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        RefreshFab.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SyncFilterIcon();
        RebuildList();
    }

    private void SyncFilterIcon()
    {
        var servers = AppServices.Vpn.Servers;
        var active = ServersPreferencesStore.FiltersActive(
            ServerProfile.VisibleTiers(servers),
            ServerProfile.VisibleProtocols(servers));
        FilterIcon.Glyph = active ? "\uE71C" : "\uE90F";
        ToolTipService.SetToolTip(FilterButton, Strings.ServersFilters);
    }

    private void RebuildList()
    {
        ListHost.Children.Clear();
        ListHost.Children.Add(new TextBlock
        {
            Text = Strings.ServersTitle,
            FontSize = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 8),
        });

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

        var favorites = ServersPreferencesStore.Favorites;
        var list = ServerProfile.FilterServers(
            vpn.Servers, ServersPreferencesStore.Tiers, ServersPreferencesStore.Protocols, favorites);
        if (list.Count == 0 && vpn.Servers.Count > 0)
        {
            ServersPreferencesStore.ResetFilters(raiseChanged: false);
            SyncFilterIcon();
            list = ServerProfile.FilterServers(
                vpn.Servers, ServersPreferencesStore.Tiers, ServersPreferencesStore.Protocols, favorites);
        }
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
                IsSelected = !profile.IsLocked && profile.Id == selected?.Id,
                IsFavorite = favorites.Contains(profile.Id),
                ReachState = reach ?? ServerReachState.Checking,
            };
            row.Click += (_, _) =>
            {
                vpn.Select(profile);
                AppServices.Snackbar.ShowSuccess(string.Format(Strings.ServerSelected, profile.TitleName));
            };
            row.Details += (_, _) => DetailsRequested?.Invoke(profile);
            row.NeedUpgrade += (_, _) => NeedUpgrade?.Invoke();
            ListHost.Children.Add(row);
        }
    }

    private void SyncSelection()
    {
        var id = AppServices.Vpn.Selected?.Id;
        foreach (var child in ListHost.Children)
            if (child is ServerRow row)
                row.IsSelected = row.Profile is { IsLocked: false } p && p.Id == id;
    }

    private void SyncReachability()
    {
        var map = AppServices.Vpn.ServerReachability;
        foreach (var child in ListHost.Children)
        {
            if (child is not ServerRow row || row.Profile is not { } p) continue;
            map.TryGetValue(p.Id, out var reach);
            row.ReachState = reach ?? ServerReachState.Checking;
        }
    }
}
