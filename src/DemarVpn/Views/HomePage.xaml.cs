using System.ComponentModel;
using DemarVpn.Core;
using DemarVpn.Domain.Vpn;
using DemarVpn.Models;
using DemarVpn.Services;
using DemarVpn.Services.Servers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DemarVpn.Views;

public sealed partial class HomePage : Page
{
    private bool _shadowWired;
    private bool _promptShown;
    private bool _promptInFlight;
    private Storyboard? _sizeStoryboard;
    private string? _flagCode;

    public event Action<ServerProfile>? DetailsRequested;

    public HomePage()
    {
        InitializeComponent();
        TitleBlock.Text = Strings.HomeTitle;
        ConnectedLabel.Text = Strings.VpnConnected;
        ServerEmpty.Text = Strings.ServerNotSelected;
        UpdateHint.Text = Strings.UpdateDownloading;
        FlagImage.ImageFailed += (_, _) => FlagImage.Source = new BitmapImage(CountryFlags.UnknownUri());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Refresh();
        SyncUpdateFab();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_shadowWired)
        {
            _shadowWired = true;
            UpdateShadow.Receivers.Add(ShadowCast);
        }
        AppServices.Vpn.PropertyChanged -= OnVpnChanged;
        AppServices.Vpn.PropertyChanged += OnVpnChanged;
        AppServices.Update.PropertyChanged -= OnUpdateChanged;
        AppServices.Update.PropertyChanged += OnUpdateChanged;
        ServersPreferencesStore.Changed -= OnPrefsChanged;
        ServersPreferencesStore.Changed += OnPrefsChanged;
        SyncUpdateFab();
        MaybePromptUpdate();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Vpn.PropertyChanged -= OnVpnChanged;
        AppServices.Update.PropertyChanged -= OnUpdateChanged;
        ServersPreferencesStore.Changed -= OnPrefsChanged;
    }

    private void OnPrefsChanged() => Ui.Run(DispatcherQueue, Refresh);

    private void OnVpnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Services.Vpn.VpnController.Throughput))
        {
            Ui.Run(DispatcherQueue, () => Throughput.Text = FormatThroughput(AppServices.Vpn.Throughput));
            return;
        }
        if (e.PropertyName is nameof(Services.Vpn.VpnController.State)
            or nameof(Services.Vpn.VpnController.Selected)
            or null)
            Ui.Run(DispatcherQueue, Refresh);
    }

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
    {
        Ui.Run(DispatcherQueue, () =>
        {
            SyncUpdateFab();
            if (e.PropertyName is nameof(AppUpdateService.State)
                or nameof(AppUpdateService.LatestVersion))
                MaybePromptUpdate();
        });
    }

    private void SyncUpdateFab()
    {
        var update = AppServices.Update;
        var show = update.State is AppUpdateState.Available
            or AppUpdateState.Downloading
            or AppUpdateState.Ready
            or AppUpdateState.Applying;
        UpdateFab.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var downloading = update.State == AppUpdateState.Downloading;
        UpdateProgressPanel.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateIcon.Visibility = downloading ? Visibility.Collapsed : Visibility.Visible;
        UpdatePercent.Text = $"{update.ProgressPercent}%";
        UpdateButton.IsEnabled = update.State == AppUpdateState.Ready;
    }

    private void MaybePromptUpdate()
    {
        if (_promptShown || _promptInFlight) return;
        if (AppServices.Update.State != AppUpdateState.Ready) return;
        if (XamlRoot is null) return;
        _ = ShowUpdateDialogAsync();
    }

    private async void OnUpdateFabClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Update.State != AppUpdateState.Ready) return;
        _promptShown = false;
        await ShowUpdateDialogAsync();
    }

    private async Task ShowUpdateDialogAsync()
    {
        if (_promptInFlight) return;
        if (XamlRoot is null) return;
        _promptInFlight = true;
        try
        {
            var latest = AppServices.Update.LatestVersion ?? "?";
            var result = await DemarDialog.ChooseAsync(
                XamlRoot,
                Strings.UpdateTitle,
                string.Format(Strings.UpdateBody, Strings.VersionName, latest),
                Strings.UpdateNow,
                Strings.UpdateNextLaunch);
            _promptShown = true;
            if (result == ContentDialogResult.Primary)
            {
                if (!AppServices.Update.ApplyNow())
                    AppServices.Snackbar.ShowError(Strings.UpdateDownloadFail);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                AppServices.Update.ScheduleNextLaunch();
            }
            else
            {
                _promptShown = false;
            }
        }
        finally
        {
            _promptInFlight = false;
        }
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        var vpn = AppServices.Vpn;
        if (vpn.State.Kind == VpnStateKind.Connected)
        {
            await Ui.ApiAsync(DispatcherQueue, () => vpn.DisconnectAsync());
            return;
        }
        if (vpn.State.IsBusy) return;

        if (!AppServices.Auth.SignedIn)
        {
            FindShell()?.ShowLogin(0);
            return;
        }
        if (vpn.Selected is null)
        {
            FindShell()?.GoToServers();
            return;
        }
        await Ui.ApiAsync(DispatcherQueue, () => vpn.ConnectAsync());
    }

    private void OnServerCardClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Vpn.Selected is { } p)
            DetailsRequested?.Invoke(p);
    }

    private ShellPage? FindShell()
    {
        for (DependencyObject? n = this; n is not null; n = VisualTreeHelper.GetParent(n))
            if (n is ShellPage s) return s;
        return null;
    }

    private void Refresh()
    {
        var vpn = AppServices.Vpn;
        var state = vpn.State;
        SyncSelectedServer(vpn.Selected);
        Throughput.Text = FormatThroughput(vpn.Throughput);
        var connected = state.Kind == VpnStateKind.Connected;
        var compact = connected || state.Kind == VpnStateKind.Disconnecting;
        var busy = state.IsBusy;
        ConnectedLabel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ToggleIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        AnimateToggleSize(compact ? 116 : 200);
        Toggle.Background = compact
            ? (Brush)Application.Current.Resources["DemarPrimaryContainerBrush"]
            : (Brush)Application.Current.Resources["DemarSurfaceHighBrush"];
        if (!busy)
        {
            ToggleIcon.Glyph = connected ? "\uE72E" : "\uE7E8";
            ToggleIcon.FontSize = compact ? 44 : 72;
        }
        Toggle.IsEnabled = !busy;
    }

    private void SyncSelectedServer(ServerProfile? profile)
    {
        if (profile is null)
        {
            ServerEmpty.Visibility = Visibility.Visible;
            ServerCard.Visibility = Visibility.Collapsed;
            return;
        }

        ServerEmpty.Visibility = Visibility.Collapsed;
        ServerCard.Visibility = Visibility.Visible;
        ServerTitle.Text = profile.TitleName;
        var sub = profile.ListSubtitle;
        ServerCity.Text = sub;
        ServerCity.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;

        if (_flagCode != profile.CountryCode)
        {
            _flagCode = profile.CountryCode;
            FlagImage.Source = new BitmapImage(CountryFlags.AssetUri(profile.CountryCode));
        }

        FavBadge.Visibility = ServersPreferencesStore.IsFavorite(profile.Id)
            ? Visibility.Visible
            : Visibility.Collapsed;
        TierBadge.BindTier(profile.Tier, large: true);
        ProtoBadge.BindProtocol(profile.Protocol, large: true);
    }

    private void AnimateToggleSize(double target)
    {
        if (Math.Abs(Toggle.Width - target) < 0.5)
        {
            Toggle.Width = Toggle.Height = target;
            return;
        }

        _sizeStoryboard?.Stop();
        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        DoubleAnimation Dim() => new()
        {
            To = target,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true,
        };
        var w = Dim();
        var h = Dim();
        Storyboard.SetTarget(w, Toggle);
        Storyboard.SetTargetProperty(w, "Width");
        Storyboard.SetTarget(h, Toggle);
        Storyboard.SetTargetProperty(h, "Height");
        _sizeStoryboard = new Storyboard();
        _sizeStoryboard.Children.Add(w);
        _sizeStoryboard.Children.Add(h);
        _sizeStoryboard.Begin();
    }

    private static string FormatThroughput(ThroughputRates r) =>
        $"↓ {r.DownloadBps / 1024:0} KB/s   ↑ {r.UploadBps / 1024:0} KB/s";
}
