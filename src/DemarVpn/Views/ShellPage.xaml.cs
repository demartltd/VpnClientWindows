using DemarVpn.Controls;
using DemarVpn.Services;
using DemarVpn.Services.Account;
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
    }

    public void ShowLogin(int returnTab = 3)
    {
        _loginReturnTab = returnTab;
        LoginSheet.Show();
    }

    public void GoToServers() => GoToTab(1);

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

    private void UpdateToolbarLayout()
    {
        var wide = ActualWidth >= (double)Application.Current.Resources["ToolbarWideBreakpoint"];
        Toolbar.SetWideLayout(wide);
        Snackbar.SetWideLayout(wide);
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
    }

    private async void OnSignOutRequested()
    {
        NotifySheet.Dismiss();
        TrafficSheet.Dismiss();
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
