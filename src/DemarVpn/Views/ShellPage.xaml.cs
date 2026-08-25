using DemarVpn.Controls;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Support;
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
        AccountTab.TrafficSheetRequested += () => TrafficSheet.Show();
        AccountTab.CheckoutRequested += OpenCheckout;
        TrafficSheet.UpgradeTrafficRequested += OnUpgradeTraffic;
        CheckoutSheet.CompareRequested += OpenPlanCompare;
        CompareSheet.BuyRequested += () => OpenCheckout(CheckoutIntent.Buy);
        AppServices.Vpn.TrafficLimitReached += () => DispatcherQueue.TryEnqueue(OpenTrafficSheet);
        AccountStore.Changed += OnAccountStoreChanged;
        LoginSheet.SignedIn += OnLoginSuccess;
        Loaded += (_, _) => UpdateToolbarLayout();
        SizeChanged += (_, _) => UpdateToolbarLayout();
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
        switch (sheet)
        {
            case DemarNavigation.AccountSheet.Buy:
                OpenCheckout(CheckoutIntent.Buy);
                break;
            case DemarNavigation.AccountSheet.Plans:
                OpenPlanCompare();
                break;
            case DemarNavigation.AccountSheet.Traffic:
                OpenTrafficSheet();
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
        GoToTab(3);
        CompareSheet.Dismiss();
        PayResultSheet.Dismiss();
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
        GoToTab(3);
        CheckoutSheet.Dismiss();
        CompareSheet.Show();
    }

    public void HandlePayReturn(bool success)
    {
        GoToTab(3);
        CheckoutSheet.Dismiss();
        CompareSheet.Dismiss();
        PayResultSheet.Show(success);
        if (!success) return;
        var now = Environment.TickCount64;
        if (now - _lastPayRefreshAt < 5_000) return;
        _lastPayRefreshAt = now;
        _ = AppServices.Vpn.RefreshServersAsync();
    }

    private void OnUpgradeTraffic()
    {
        TrafficSheet.Dismiss();
        _ = OpenCheckoutDelayedAsync(CheckoutIntent.UpgradeTraffic);
    }

    private async Task OpenCheckoutDelayedAsync(CheckoutIntent intent)
    {
        await Task.Delay(400);
        OpenCheckout(intent);
    }

    private void OpenTrafficSheet()
    {
        if (!AppServices.Auth.SignedIn)
        {
            ShowLogin(3);
            return;
        }
        GoToTab(3);
        TrafficSheet.Show();
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
        DispatcherQueue.TryEnqueue(() =>
        {
            AccountTab.Refresh();
            if (NotifySheet.Visibility == Visibility.Visible) NotifySheet.BindFromStore();
            if (TrafficSheet.Visibility == Visibility.Visible) TrafficSheet.BindFromStore();
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
        GoToTab(target);
        AccountTab.Refresh();
        await AppServices.Vpn.RefreshServersAsync();
        if (target == 1) ServersTab.OnTabVisible();
        MaybeOpenPendingSupport();
        MaybeOpenPendingCheckout();
    }

    private async void OnSignOutRequested()
    {
        NotifySheet.Dismiss();
        TrafficSheet.Dismiss();
        CheckoutSheet.Dismiss();
        CompareSheet.Dismiss();
        PayResultSheet.Dismiss();
        await AppServices.SignOutFullyAsync();
        AccountTab.Refresh();
        GoToTab(0);
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

    internal void GoBack()
    {
        if (OnOverlayBack()) return;
        if (Frame?.Parent is FrameworkElement grid &&
            grid.Parent is ShellPage shell)
            shell.CloseOverlay();
        else if (Frame is not null)
            Frame.Content = null;
    }
}
