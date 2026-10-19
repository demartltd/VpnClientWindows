using DemarVpn.Controls;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Support;
using DemarVpn.Services.Vpn;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Views;

public sealed partial class ShellPage : Page
{
    private readonly Page[] _tabs;
    private int _tab;
    private int _loginReturnTab = 3;
    private bool _openSupportAfterLogin;
    private SupportRoute? _pendingSupportRoute;
    private CheckoutIntent? _pendingCheckout;
    private long _lastPayRefreshAt;

    public ShellPage()
    {
        InitializeComponent();
        _tabs = [HomeTab, ServersTab, SettingsTab, AccountTab];
        SettingsTab.AppSettingsRequested += () => ShowOverlay(typeof(AppSettingsPage));
        SettingsTab.ServiceMenuRequested += () => ShowOverlay(typeof(ServiceMenuPage));
        SettingsTab.DocumentsRequested += () => DocumentsSheet.Show();
        SettingsTab.SupportRequested += () => OpenSupport();
        SettingsTab.SignInRequested += () => ShowLogin(2);
        DocumentsSheet.DocumentSelected += doc => ShowOverlay(typeof(DocumentPage), doc);
        AccountTab.SignOutRequested += OnSignOutRequested;
        AccountTab.NotifySheetRequested += () => NotifySheet.Show();
        AccountTab.TrafficSheetRequested += () => OpenTrafficOrFreeAlert();
        AccountTab.CheckoutRequested += OpenCheckout;
        ServersTab.NeedUpgrade += () => OpenCheckout(CheckoutIntent.Buy);
        TrafficSheet.UpgradeTrafficRequested += () => OpenCheckout(CheckoutIntent.UpgradeTraffic);
        InfoSheet.ActionRequested += kind =>
            OpenCheckout(kind == SubscriptionAlertKind.Expiry ? CheckoutIntent.Renew : CheckoutIntent.Buy);
        CheckoutSheet.CompareRequested += OpenPlanCompare;
        CompareSheet.BuyRequested += () => OpenCheckout(CheckoutIntent.Buy);
        AppServices.Vpn.TrafficLimitReached += OnTrafficLimitReached;
        AccountStore.Changed += OnAccountStoreChanged;
        LoginSheet.SignedIn += OnLoginSuccess;
        Loaded += (_, _) =>
        {
            UpdateToolbarLayout();
            EvaluateSubscriptionAlerts();
        };
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => UpdateToolbarLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AccountStore.Changed -= OnAccountStoreChanged;
        AppServices.Vpn.TrafficLimitReached -= OnTrafficLimitReached;
    }

    private void OnTrafficLimitReached() => Ui.Run(DispatcherQueue, OpenTrafficOrFreeAlert);

    public void OpenAppSettings()
    {
        GoToTab(2);
        ShowOverlay(typeof(AppSettingsPage));
    }

    public void OnExternalSignIn()
    {
        LoginSheet.Dismiss();
        GoToTab(_loginReturnTab);
        AccountTab.Refresh();
        MaybeOpenPendingSupport();
        MaybeOpenPendingCheckout();
    }

    public void ShowLogin(int returnTab = 3)
    {
        _loginReturnTab = returnTab;
        LoginSheet.Show();
    }

    public void GoToServers() => GoToTab(1);

    public void OpenAccountSheet(DemarNavigation.AccountSheet sheet)
    {
        if (!AppServices.Auth.SignedIn)
        {
            if (sheet == DemarNavigation.AccountSheet.Buy)
                _pendingCheckout = CheckoutIntent.Buy;
            ShowLogin(3);
            return;
        }
        GoToTab(3);
        switch (sheet)
        {
            case DemarNavigation.AccountSheet.Buy:
                OpenCheckout(CheckoutIntent.Buy);
                break;
            case DemarNavigation.AccountSheet.Plans:
                OpenPlanCompare();
                break;
            case DemarNavigation.AccountSheet.Traffic:
                OpenTrafficOrFreeAlert();
                break;
        }
    }

    public void OpenCheckout(CheckoutIntent intent)
    {
        if (!AppServices.Auth.SignedIn)
        {
            _pendingCheckout = intent;
            ShowLogin(3);
            return;
        }
        DismissAccountSheets(keepCheckout: true);
        var plan = AccountStore.Account?.VpnSubscription ?? AuthSessionStore.Current?.VpnSubscription;
        CheckoutSheet.Show(intent, plan);
    }

    public void OpenPlanCompare()
    {
        if (!AppServices.Auth.SignedIn)
        {
            ShowLogin(3);
            return;
        }
        DismissAccountSheets(keepCompare: true);
        CompareSheet.Show();
    }

    public void HandlePayReturn(bool success)
    {
        GoToTab(3);
        DismissAccountSheets(keepPayResult: true);
        PayResultSheet.Show(success);
        if (!success) return;
        var now = Environment.TickCount64;
        if (now - _lastPayRefreshAt < 5_000) return;
        _lastPayRefreshAt = now;
        _ = AppServices.Vpn.RefreshServersAsync();
    }

    public void PresentPayResult(bool success)
    {
        DismissAccountSheets(keepPayResult: true);
        PayResultSheet.Show(success);
    }

    public void OpenTrafficSheet()
    {
        if (!AppServices.Auth.SignedIn)
        {
            ShowLogin(3);
            return;
        }
        GoToTab(3);
        DismissAccountSheets(keepTraffic: true);
        TrafficSheet.Show();
    }

    /// <summary>Paid → traffic pie; free → FreeTraffic upsell.</summary>
    public void OpenTrafficOrFreeAlert()
    {
        if (VpnController.IsFreeSubscription())
            OpenSubscriptionAlert(SubscriptionAlertKind.FreeTraffic);
        else
            OpenTrafficSheet();
    }

    public void OpenSubscriptionAlert(SubscriptionAlertKind kind)
    {
        if (!AppServices.Auth.SignedIn)
        {
            ShowLogin(3);
            return;
        }
        DismissAccountSheets(keepInfo: true);
        InfoSheet.Show(kind, SubscriptionAlertHighlight(kind));
    }

    /// <summary>Cached account/traffic only — call on activate / store change.</summary>
    public void EvaluateSubscriptionAlerts()
    {
        if (!AppServices.Auth.SignedIn || HasAccountSheetOpen()) return;

        if (!VpnController.IsFreeSubscription())
        {
            var exp = AccountStore.Account?.VpnExpiresAt ?? AuthSessionStore.Current?.VpnExpiresAt;
            if (exp is null
                || !SubscriptionAlertStore.ShouldShowExpiry(exp)
                || !SubscriptionAlertStore.CanShowExpiry())
                return;
            SubscriptionAlertStore.MarkExpiryShown();
            OpenSubscriptionAlert(SubscriptionAlertKind.Expiry);
            return;
        }

        var t = AccountStore.Traffic;
        if (t is null
            || !SubscriptionAlertStore.ShouldShowTraffic(t.Total)
            || !SubscriptionAlertStore.CanShowTraffic())
            return;
        SubscriptionAlertStore.MarkTrafficShown();
        OpenSubscriptionAlert(SubscriptionAlertKind.FreeTraffic);
    }

    static string SubscriptionAlertHighlight(SubscriptionAlertKind kind)
    {
        switch (kind)
        {
            case SubscriptionAlertKind.Expiry:
            {
                var iso = AccountStore.Account?.VpnExpiresAt ?? AuthSessionStore.Current?.VpnExpiresAt;
                if (iso is not null
                    && SubscriptionAlertStore.MillisUntilExpiry(iso) is { } ms
                    && ms > 0)
                    return SubscriptionAlertStore.FormatRemaining(ms);
                return SubscriptionAlertStore.FormatRemaining(2L * 60 * 60 * 1000);
            }
            default:
            {
                if (AccountStore.Traffic is { } t)
                    return VpnAccountApi.FormatBytes(Math.Max(0L, t.LimitBytes - t.Total));
                return VpnAccountApi.FormatBytes(500L * 1024 * 1024);
            }
        }
    }

    bool HasAccountSheetOpen() =>
        TrafficSheet.Visibility == Visibility.Visible
        || InfoSheet.IsOpen
        || CheckoutSheet.Visibility == Visibility.Visible
        || CompareSheet.Visibility == Visibility.Visible
        || PayResultSheet.Visibility == Visibility.Visible;

    void DismissAccountSheets(
        bool keepTraffic = false,
        bool keepInfo = false,
        bool keepCheckout = false,
        bool keepCompare = false,
        bool keepPayResult = false)
    {
        if (!keepTraffic) TrafficSheet.Dismiss();
        if (!keepInfo) InfoSheet.Dismiss();
        if (!keepCheckout) CheckoutSheet.Dismiss();
        if (!keepCompare) CompareSheet.Dismiss();
        if (!keepPayResult) PayResultSheet.Dismiss();
    }

    public void OpenSupport(SupportRoute? route = null)
    {
        if (!AppServices.Auth.SignedIn)
        {
            _pendingSupportRoute = route;
            _openSupportAfterLogin = true;
            ShowLogin(2);
            return;
        }
        GoToTab(2);
        ShowOverlay(typeof(SupportPage), route);
    }

    private void MaybeOpenPendingSupport()
    {
        if (!_openSupportAfterLogin && _pendingSupportRoute is null) return;
        _openSupportAfterLogin = false;
        var route = _pendingSupportRoute;
        _pendingSupportRoute = null;
        ShowOverlay(typeof(SupportPage), route);
    }

    private void MaybeOpenPendingCheckout()
    {
        if (_pendingCheckout is not { } intent) return;
        _pendingCheckout = null;
        OpenCheckout(intent);
    }

    private void UpdateToolbarLayout()
    {
        var wide = ActualWidth >= (double)Application.Current.Resources["ToolbarWideBreakpoint"];
        Toolbar.SetWideLayout(wide);
        Snackbar.SetWideLayout(wide);
        CheckoutSheet.SetWideLayout(wide);
    }

    private void OnAccountStoreChanged() =>
        Ui.Run(DispatcherQueue, () =>
        {
            AccountTab.Refresh();
            if (NotifySheet.Visibility == Visibility.Visible) NotifySheet.BindFromStore();
            if (TrafficSheet.Visibility == Visibility.Visible) TrafficSheet.BindFromStore();
            EvaluateSubscriptionAlerts();
        });

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = Toolbar.SelectedIndex;
        // Servers (1) and Account (3) require sign-in — open login sheet, do not switch tab.
        if ((index is 1 or 3) && !AppServices.Auth.SignedIn)
        {
            Toolbar.Select(_tab);
            ShowLogin(index);
            return;
        }
        GoToTab(index);
    }

    private async void OnLoginSuccess(object? sender, EventArgs e)
    {
        var target = _loginReturnTab is 1 or 3 ? _loginReturnTab : 3;
        if (_openSupportAfterLogin || _pendingSupportRoute is not null) target = 2;
        try
        {
            GoToTab(target);
            AccountTab.Refresh();
            await Ui.ApiAsync(DispatcherQueue, () => AppServices.Vpn.RefreshServersAsync());
            if (target == 1) ServersTab.OnTabVisible();
            MaybeOpenPendingSupport();
            MaybeOpenPendingCheckout();
            EvaluateSubscriptionAlerts();
        }
        catch (Exception ex)
        {
            AppLog.AppendCrash($"shell: login success failed — {ex}");
            if (target == 1) ServersTab.OnTabVisible();
            MaybeOpenPendingSupport();
            MaybeOpenPendingCheckout();
        }
    }

    private async void OnSignOutRequested()
    {
        AppLog.AppendCrash("shell: signing out");
        try
        {
            NotifySheet.Dismiss();
            DismissAccountSheets();
            await Ui.ApiAsync(DispatcherQueue, () => AppServices.SignOutFullyAsync());
            AccountTab.Refresh();
            GoToTab(0);
        }
        catch (Exception ex)
        {
            AppLog.AppendCrash($"shell: sign-out failed — {ex}");
            AccountTab.Refresh();
            GoToTab(0);
        }
    }

    private void GoToTab(int index)
    {
        _tab = index;
        Toolbar.Select(index);
        for (var i = 0; i < _tabs.Length; i++)
            _tabs[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        if (index == 1) ServersTab.OnTabVisible();
        if (index == 3) AccountTab.Refresh();
    }

    private void ShowOverlay(Type pageType, object? parameter = null)
    {
        MainLayer.Visibility = Visibility.Collapsed;
        BottomChrome.Visibility = Visibility.Collapsed;
        Snackbar.Margin = (Thickness)Application.Current.Resources["SnackbarOverlayMargin"];
        OverlayFrame.Visibility = Visibility.Visible;
        OverlayFrame.Navigate(pageType, parameter);
    }

    public void CloseOverlay()
    {
        if (OverlayFrame.Content is OverlayPage overlay)
            overlay.PrepareClose();
        OverlayFrame.Content = null;
        OverlayFrame.Visibility = Visibility.Collapsed;
        MainLayer.Visibility = Visibility.Visible;
        BottomChrome.Visibility = Visibility.Visible;
        Snackbar.Margin = (Thickness)Application.Current.Resources["ToolbarSnackbarMargin"];
    }
}

public abstract class OverlayPage : Page
{
    /// <summary>Return true if back was handled (e.g. chat → list).</summary>
    protected virtual bool OnOverlayBack() => false;

    /// <summary>Called before shell clears overlay content (Frame.Content=null may skip OnNavigatedFrom).</summary>
    internal virtual void PrepareClose() { }

    protected ShellPage? FindShell()
    {
        if (Frame?.Parent is FrameworkElement grid && grid.Parent is ShellPage shell)
            return shell;
        return null;
    }

    internal void GoBack()
    {
        if (OnOverlayBack()) return;
        if (FindShell() is { } shell)
            shell.CloseOverlay();
        else if (Frame is not null)
            Frame.Content = null;
    }
}
