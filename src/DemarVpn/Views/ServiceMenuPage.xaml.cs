using System.ComponentModel;
using DemarVpn.Controls;
using DemarVpn.Models;
using DemarVpn.Services;
using DemarVpn.Services.Account;
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
        InfoSheetsHeader.Text = Strings.ServiceInfoSheets;
        ExpiryBtn.Title = Strings.ServiceInfoExpiry;
        ExpiryBtn.Subtitle = Strings.ServiceInfoExpirySub;
        FreeTrafficBtn.Title = Strings.ServiceInfoFreeTraffic;
        FreeTrafficBtn.Subtitle = Strings.ServiceInfoFreeTrafficSub;
        TrafficBtn.Title = Strings.ServiceInfoTraffic;
        TrafficBtn.Subtitle = Strings.ServiceInfoTrafficSub;
        PayOkBtn.Title = Strings.ServiceInfoPayOk;
        PayOkBtn.Subtitle = Strings.ServiceInfoPayOkSub;
        PayFailBtn.Title = Strings.ServiceInfoPayFail;
        PayFailBtn.Subtitle = Strings.ServiceInfoPayFailSub;
        NameRow.Key = Strings.ServiceName;
        NameRow.Value = Strings.AppName;
        PackageRow.Key = Strings.ServicePackage;
        PackageRow.Value = AppPackageInfo.PackageId;
        VersionRow.Key = Strings.ServiceVersion;
        VersionRow.Value = AppPackageInfo.VersionDetail;
        LatestVersionRow.Key = Strings.ServiceLatestVersion;
        LatestVersionRow.Value = "…";
        PlatformRow.Key = Strings.ServicePlatform;
        PlatformRow.Value = Strings.PlatformWindows;
        CoreRow.Key = Strings.ServiceCore;
        CoreRow.Value = AppServices.SingBoxLabel;
        RouteRow.Key = Strings.ServiceRoute;
        IpRow.Key = Strings.ServiceIp;
        CountryRow.Key = Strings.ServiceCountry;
        ToolTipService.SetToolTip(RefreshBtn, Strings.ServiceRefresh);
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
        if (AppServices.Update.LatestVersion is { } cached)
        {
            LatestVersionRow.Value = cached;
            return;
        }

        LatestVersionRow.Value = "…";
        for (var i = 0; i < 2; i++)
        {
            if (i > 0)
            {
                await Task.Delay(400).ConfigureAwait(false);
                await Ui.SwitchTo(DispatcherQueue);
            }
            var release = await Ui.ApiAsync(DispatcherQueue,
                () => WindowsReleaseApi.FetchAsync(UpdateChannelStore.PreferBeta));
            if (release?.Version is { } v)
            {
                LatestVersionRow.Value = v;
                return;
            }
        }
        LatestVersionRow.Value = "—";
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
        await Ui.SwitchTo(DispatcherQueue);
        DeviceBusy.Visibility = Visibility.Visible;
        DevicePanel.Visibility = Visibility.Collapsed;
        DevicePanel.Children.Clear();
        var rows = await Ui.ApiAsync(DispatcherQueue, () => DeviceIdentity.LoadAsync());
        foreach (var (key, value) in rows)
            DevicePanel.Children.Add(new DemarKvRow { Key = key, Value = value, KeyWidth = 108 });
        DeviceBusy.Visibility = Visibility.Collapsed;
        DevicePanel.Visibility = Visibility.Visible;
    }

    private async Task ReloadEgressAsync()
    {
        _egressCts?.Cancel();
        _egressCts?.Dispose();
        _egressCts = new CancellationTokenSource();
        var ct = _egressCts.Token;

        void SetRows(string route, string ip, string country) =>
            Ui.Run(DispatcherQueue, () =>
            {
                if (ct.IsCancellationRequested) return;
                RouteRow.Value = route;
                IpRow.Value = ip;
                CountryRow.Value = country;
            });

        SetRows("…", "…", "…");
        foreach (var row in _hostRows)
            Ui.Run(DispatcherQueue, () => { if (!ct.IsCancellationRequested) row.Value = "…"; });
        Ui.Run(DispatcherQueue, StartRefreshSpin);

        try
        {
            var state = AppServices.Vpn.State;
            var route = await Ui.ApiAsync(DispatcherQueue,
                () => PublicEgressDiagnostics.ResolveRouteAsync(state));
            if (ct.IsCancellationRequested) return;
            var useTunnel = PublicEgressDiagnostics.ShouldUseTunnel(route);
            var geo = await Ui.ApiAsync(DispatcherQueue,
                () => PublicEgressDiagnostics.FetchGeoAsync(useTunnel, ct));
            if (ct.IsCancellationRequested) return;
            SetRows(
                PublicEgressDiagnostics.RouteLabel(route),
                PublicEgressDiagnostics.CensorPublicIpv4(geo.Ip),
                geo.Country);

            await PublicEgressDiagnostics.ProbeAllHostsAsync(useTunnel, (i, probe) =>
            {
                Ui.Run(DispatcherQueue, () =>
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

    private void OnInfoExpiry(object sender, EventArgs e) =>
        FindShell()?.OpenSubscriptionAlert(SubscriptionAlertKind.Expiry);

    private void OnInfoFreeTraffic(object sender, EventArgs e) =>
        FindShell()?.OpenSubscriptionAlert(SubscriptionAlertKind.FreeTraffic);

    private void OnInfoTraffic(object sender, EventArgs e) =>
        FindShell()?.OpenTrafficSheet();

    private void OnInfoPayOk(object sender, EventArgs e) =>
        FindShell()?.PresentPayResult(true);

    private void OnInfoPayFail(object sender, EventArgs e) =>
        FindShell()?.PresentPayResult(false);
}
