using System.ComponentModel;
using DemarVpn.Models;
using DemarVpn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Views;

public sealed partial class HomePage : Page
{
    private bool _shadowWired;
    private bool _promptShown;
    private bool _promptInFlight;
    private Storyboard? _sizeStoryboard;

    public HomePage()
    {
        InitializeComponent();
        TitleBlock.Text = Strings.HomeTitle;
        ConnectedLabel.Text = Strings.VpnConnected;
        UpdateHint.Text = Strings.UpdateDownloading;
        AppServices.Vpn.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(Refresh);
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
        AppServices.Update.PropertyChanged += OnUpdateChanged;
        SyncUpdateFab();
        MaybePromptUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Update.PropertyChanged -= OnUpdateChanged;
    }

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
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
                // Dismiss without choice — allow FAB to reopen.
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
            await vpn.DisconnectAsync();
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
        await vpn.ConnectAsync();
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
        ServerName.Text = vpn.Selected is null ? Strings.ServerNotSelected : vpn.Selected.ListTitle;
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
