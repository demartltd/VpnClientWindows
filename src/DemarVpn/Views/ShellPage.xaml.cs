using DemarVpn.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Views;

public sealed partial class ShellPage : Page
{
    private readonly Page[] _tabs;

    public ShellPage()
    {
        InitializeComponent();
        _tabs = [HomeTab, ServersTab, SettingsTab, AccountTab];
        SettingsTab.AppSettingsRequested += () => ShowOverlay(typeof(AppSettingsPage));
        SettingsTab.ServiceMenuRequested += () => ShowOverlay(typeof(ServiceMenuPage));
        Loaded += (_, _) => UpdateToolbarLayout();
        SizeChanged += (_, _) => UpdateToolbarLayout();
    }

    private void UpdateToolbarLayout()
    {
        var breakpoint = (double)Application.Current.Resources["ToolbarWideBreakpoint"];
        Toolbar.SetWideLayout(ActualWidth >= breakpoint);
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e) =>
        SelectTab(Toolbar.SelectedIndex);

    private void SelectTab(int index)
    {
        for (var i = 0; i < _tabs.Length; i++)
            _tabs[i].Visibility = i == index ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (index == 0) AppServices.Vpn.TickThroughput();
    }

    private void ShowOverlay(Type pageType)
    {
        MainLayer.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        BottomChrome.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        Snackbar.Margin = (Thickness)Application.Current.Resources["SnackbarOverlayMargin"];
        OverlayFrame.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        OverlayFrame.Navigate(pageType);
    }

    public void CloseOverlay()
    {
        OverlayFrame.Content = null;
        OverlayFrame.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        MainLayer.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        BottomChrome.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        Snackbar.Margin = (Thickness)Application.Current.Resources["ToolbarSnackbarMargin"];
    }
}

public abstract class OverlayPage : Page
{
    protected void GoBack()
    {
        if (Frame?.Parent is Microsoft.UI.Xaml.FrameworkElement grid &&
            grid.Parent is ShellPage shell)
            shell.CloseOverlay();
        else if (Frame is not null)
            Frame.Content = null;
    }
}
