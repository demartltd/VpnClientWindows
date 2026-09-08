using System.ComponentModel;
using DemarVpn.Controls;
using DemarVpn.Models;
using DemarVpn.Services;
using DemarVpn.Services.Device;
using DemarVpn.Services.Network;
using DemarVpn.Services.Vpn;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Views;

public sealed partial class ServiceMenuPage : OverlayPage
{
    private readonly DemarKvRow[] _hostRows;
    private Storyboard? _spinStoryboard;
    private CancellationTokenSource? _egressCts;

    public ServiceMenuPage()
    {
        InitializeComponent();
        Chrome.Title = Strings.ServiceMenuTitle;
        AboutHeader.Text = Strings.ServiceAbout;
        InternetHeader.Text = Strings.ServiceInternet;
        DeviceHeader.Text = Strings.ServiceDevice;
        DebugHeader.Text = Strings.ServiceDebug;
        NameRow.Value = Strings.AppName;
        PackageRow.Value = AppPackageInfo.PackageId;
        VersionRow.Value = AppPackageInfo.VersionDetail;
        LatestVersionRow.Value = "…";
        PlatformRow.Value = Strings.PlatformWindows;
        CoreRow.Key = Strings.ServiceCore;
        CoreRow.Value = AppServices.SingBoxLabel;
        RouteRow.Key = Strings.ServiceRoute;
        IpRow.Key = Strings.ServiceIp;
        CountryRow.Key = Strings.ServiceCountry;
        RestartBtn.Content = Strings.ServiceRestartCore;
        CrashBtn.Content = Strings.ServiceCrash;

        _hostRows = PublicEgressDiagnostics.ProbeHosts
            .Select(h => new DemarKvRow { Key = h, Value = "…", KeyWidth = 120 })
            .ToArray();
        foreach (var row in _hostRows) HostRowsPanel.Children.Add(row);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppServices.Vpn.PropertyChanged += OnVpnChanged;
        _ = ReloadDeviceAsync();
        _ = ReloadEgressAsync();
        _ = ReloadLatestVersionAsync();
    }

    private async Task ReloadLatestVersionAsync()
    {
        void Set(string v) => DispatcherQueue.TryEnqueue(() => LatestVersionRow.Value = v);

        if (AppServices.Update.LatestVersion is { } cached)
        {
            Set(cached);
            return;
        }

        Set("…");
        for (var i = 0; i < 2; i++)
        {
            if (i > 0) await Task.Delay(400);
            var release = await WindowsReleaseApi.FetchAsync(UpdateChannelStore.PreferBeta);
            if (release?.Version is { } v)
            {
                Set(v);
                return;
            }
        }
        Set("—");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Vpn.PropertyChanged -= OnVpnChanged;
        _spinStoryboard?.Stop();
        _egressCts?.Cancel();
        _egressCts?.Dispose();
        _egressCts = null;
    }

    private void OnVpnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VpnController.State))
            _ = ReloadEgressAsync();
    }

    private async Task ReloadDeviceAsync()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DeviceBusy.Visibility = Visibility.Visible;
            DevicePanel.Visibility = Visibility.Collapsed;
            DevicePanel.Children.Clear();
        });
        var rows = await DeviceIdentity.LoadAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var (key, value) in rows)
                DevicePanel.Children.Add(new DemarKvRow { Key = key, Value = value, KeyWidth = 108 });
            DeviceBusy.Visibility = Visibility.Collapsed;
            DevicePanel.Visibility = Visibility.Visible;
        });
    }

    private async Task ReloadEgressAsync()
    {
        _egressCts?.Cancel();
        _egressCts?.Dispose();
        _egressCts = new CancellationTokenSource();
        var ct = _egressCts.Token;

        void SetRows(string route, string ip, string country)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ct.IsCancellationRequested) return;
                RouteRow.Value = route;
                IpRow.Value = ip;
                CountryRow.Value = country;
            });
        }

        SetRows("…", "…", "…");
        foreach (var row in _hostRows)
            DispatcherQueue.TryEnqueue(() => { if (!ct.IsCancellationRequested) row.Value = "…"; });
        StartRefreshSpin();

        try
        {
            var state = AppServices.Vpn.State;
            var route = await PublicEgressDiagnostics.ResolveRouteAsync(state);
            if (ct.IsCancellationRequested) return;
            var useTunnel = PublicEgressDiagnostics.ShouldUseTunnel(route);
            var geo = await PublicEgressDiagnostics.FetchGeoAsync(useTunnel, ct);
            if (ct.IsCancellationRequested) return;
            SetRows(
                PublicEgressDiagnostics.RouteLabel(route),
                PublicEgressDiagnostics.CensorPublicIpv4(geo.Ip),
                geo.Country);

            await PublicEgressDiagnostics.ProbeAllHostsAsync(useTunnel, (i, probe) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ct.IsCancellationRequested)
                        _hostRows[i].Value = PublicEgressDiagnostics.ProbeLabel(probe);
                });
            }, ct);
        }
        catch (OperationCanceledException) { }
    }

    private void StartRefreshSpin()
    {
        _spinStoryboard?.Stop();
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(2.5),
            RepeatBehavior = new RepeatBehavior(1),
        };
        Storyboard.SetTarget(anim, RefreshSpin);
        Storyboard.SetTargetProperty(anim, "Angle");
        _spinStoryboard = new Storyboard();
        _spinStoryboard.Children.Add(anim);
        _spinStoryboard.Begin();
    }

    private void OnEgressRefresh(object sender, RoutedEventArgs e) => _ = ReloadEgressAsync();

    private async void OnRestartCore(object sender, RoutedEventArgs e)
    {
        RestartBtn.IsEnabled = false;
        try
        {
            await AppServices.Vpn.HardRestartCoreAsync();
            AppServices.Snackbar.ShowSuccess(Strings.ServiceRestartOk);
        }
        catch (Exception ex)
        {
            AppServices.Snackbar.ShowError(ex.Message);
        }
        finally
        {
            RestartBtn.IsEnabled = true;
        }
    }

    private void OnCrash(object sender, RoutedEventArgs e) =>
        throw new InvalidOperationException("service menu crash");

    private void OnSnackInfo(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Info);
    private void OnSnackSuccess(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Success);
    private void OnSnackError(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Error);
    private void OnSnackWarning(object sender, RoutedEventArgs e) => ShowSnack(SnackKind.Warning);

    private static void ShowSnack(SnackKind kind) =>
        AppServices.Snackbar.Show(kind, string.Format(Strings.ServiceSnackDemo, kind));
}
