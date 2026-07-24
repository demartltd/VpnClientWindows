using DemarVpn.Controls;
using DemarVpn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Views;

public sealed partial class ShellPage : Page
{
    private readonly Page[] _tabs;
    private int _tab;
    private int _loginReturnTab = 3;

    public ShellPage()
    {
        InitializeComponent();
        _tabs = [HomeTab, ServersTab, SettingsTab, AccountTab];
        SettingsTab.AppSettingsRequested += () => ShowOverlay(typeof(AppSettingsPage));
        SettingsTab.ServiceMenuRequested += () => ShowOverlay(typeof(ServiceMenuPage));
        AccountTab.SignOutRequested += OnSignOutRequested;
        LoginSheet.SignedIn += OnLoginSuccess;
        Loaded += (_, _) => UpdateToolbarLayout();
        SizeChanged += (_, _) => UpdateToolbarLayout();
    }

    public void OnExternalSignIn()
    {
        LoginSheet.Dismiss();
        GoToTab(_loginReturnTab);
        AccountTab.Refresh();
    }

    public void ShowLogin(int returnTab = 3)
    {
        _loginReturnTab = returnTab;
        LoginSheet.Show();
    }

    public void GoToServers() => GoToTab(1);

    private void UpdateToolbarLayout()
    {
        var breakpoint = (double)Application.Current.Resources["ToolbarWideBreakpoint"];
        Toolbar.SetWideLayout(ActualWidth >= breakpoint);
    }

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
        GoToTab(target);
        AccountTab.Refresh();
        await AppServices.Vpn.RefreshServersAsync();
        if (target == 1) ServersTab.OnTabVisible();
    }

    private async void OnSignOutRequested()
    {
        if (AppServices.Vpn.State.Kind is VpnStateKind.Connected or VpnStateKind.Connecting)
            await AppServices.Vpn.DisconnectAsync();
        AppServices.Vpn.ClearServers();
        GoToTab(0);
    }

    private void GoToTab(int index)
    {
        _tab = index;
        Toolbar.Select(index);
        for (var i = 0; i < _tabs.Length; i++)
            _tabs[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        if (index == 0) AppServices.Vpn.TickThroughput();
        if (index == 1) ServersTab.OnTabVisible();
        if (index == 3) AccountTab.Refresh();
    }

    private void ShowOverlay(Type pageType)
    {
        MainLayer.Visibility = Visibility.Collapsed;
        BottomChrome.Visibility = Visibility.Collapsed;
        Snackbar.Margin = (Thickness)Application.Current.Resources["SnackbarOverlayMargin"];
        OverlayFrame.Visibility = Visibility.Visible;
        OverlayFrame.Navigate(pageType);
    }

    public void CloseOverlay()
    {
        OverlayFrame.Content = null;
        OverlayFrame.Visibility = Visibility.Collapsed;
        MainLayer.Visibility = Visibility.Visible;
        BottomChrome.Visibility = Visibility.Visible;
        Snackbar.Margin = (Thickness)Application.Current.Resources["ToolbarSnackbarMargin"];
    }
}

public abstract class OverlayPage : Page
{
    protected void GoBack()
    {
        if (Frame?.Parent is FrameworkElement grid &&
            grid.Parent is ShellPage shell)
            shell.CloseOverlay();
        else if (Frame is not null)
            Frame.Content = null;
    }
}
