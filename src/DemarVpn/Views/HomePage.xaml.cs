using DemarVpn.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        TitleBlock.Text = Strings.HomeTitle;
        ConnectedLabel.Text = Strings.VpnConnected;
        AppServices.Vpn.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
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

        if (Frame?.Parent is not FrameworkElement { Parent: ShellPage shell })
            return;

        if (!AppServices.Auth.SignedIn)
        {
            shell.ShowLogin(returnTab: 0);
            return;
        }
        if (vpn.Selected is null)
        {
            shell.GoToServers();
            return;
        }
        await vpn.ConnectAsync();
    }

    private void Refresh()
    {
        var vpn = AppServices.Vpn;
        var state = vpn.State;
        ServerName.Text = vpn.Selected is null ? Strings.ServerNotSelected : vpn.Selected.ListTitle;
        Throughput.Text = FormatThroughput(vpn.Throughput);
        var connected = state.Kind == VpnStateKind.Connected;
        var busy = state.IsBusy;
        ConnectedLabel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Toggle.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        Toggle.Width = Toggle.Height = connected || state.Kind == VpnStateKind.Disconnecting ? 116 : 200;
        Toggle.Background = connected || state.Kind == VpnStateKind.Disconnecting
            ? (Brush)Application.Current.Resources["DemarPrimaryContainerBrush"]
            : (Brush)Application.Current.Resources["DemarSurfaceHighBrush"];
        ToggleIcon.Glyph = connected ? "\uE72E" : "\uE7E8";
        ToggleIcon.FontSize = connected ? 44 : 72;
        Toggle.IsEnabled = !busy;
    }

    private static string FormatThroughput(ThroughputRates r) =>
        $"↓ {r.DownloadBps / 1024:0} KB/s   ↑ {r.UploadBps / 1024:0} KB/s";
}
