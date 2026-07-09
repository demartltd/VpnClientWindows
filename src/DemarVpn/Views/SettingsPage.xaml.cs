using DemarVpn.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace DemarVpn.Views;

public sealed partial class SettingsPage : Page
{
    public event Action? AppSettingsRequested;
    public event Action? ServiceMenuRequested;

    private int _tapCount;
    private DateTimeOffset _tapWindow;

    public SettingsPage()
    {
        InitializeComponent();
        VersionLine.Text = $"Demar VPN - {Strings.VersionName} - {Strings.PlatformWindows}";
        SingBoxLine.Text = $"sing-box - {AppServices.SingBoxLabel}";
        BuildSocialLinks();
    }

    private void BuildSocialLinks()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (iconKey, url) in SocialLinks.All)
            panel.Children.Add(new IconLinkChip { IconKey = iconKey, Url = url });
        SocialLinksHost.Content = panel;
    }

    private void OnAppSettings(object sender, RoutedEventArgs e) => AppSettingsRequested?.Invoke();
    private void OnSoon(object sender, RoutedEventArgs e) => AppServices.Snackbar.ShowSoon();
    private void OnShare(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(Strings.ShareAppPitch);
        Clipboard.SetContent(data);
        AppServices.Snackbar.ShowInfo("Text copied to clipboard");
    }

    private void OnVersionTap(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _tapWindow).TotalMilliseconds > 2000) _tapCount = 0;
        _tapWindow = now;
        if (++_tapCount >= 3)
        {
            _tapCount = 0;
            ServiceMenuRequested?.Invoke();
        }
    }
}
