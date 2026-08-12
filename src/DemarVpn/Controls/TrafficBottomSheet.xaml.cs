using DemarVpn.Services.Account;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Controls;

public sealed partial class TrafficBottomSheet : UserControl
{
    public TrafficBottomSheet()
    {
        InitializeComponent();
        Chrome.Dismissed += () => Visibility = Visibility.Collapsed;
    }

    public void Show()
    {
        Visibility = Visibility.Visible;
        Chrome.Show();
        BindFromStore();
    }

    public void Dismiss()
    {
        Chrome.Dismiss();
        Visibility = Visibility.Collapsed;
    }

    public void BindFromStore()
    {
        var traffic = AccountStore.Traffic;
        var loading = AccountStore.Loading;
        if (traffic is null && loading)
        {
            StatusText.Text = Strings.Loading;
            StatusText.Visibility = Visibility.Visible;
            Pie.Visibility = Visibility.Collapsed;
            return;
        }
        if (traffic is null)
        {
            StatusText.Text = Strings.AccountTrafficUnavailable;
            StatusText.Visibility = Visibility.Visible;
            Pie.Visibility = Visibility.Collapsed;
            return;
        }
        StatusText.Visibility = Visibility.Collapsed;
        Pie.Visibility = Visibility.Visible;
        Pie.Bind(traffic.Servers, Strings.AccountTrafficOther, VpnAccountApi.FormatBytes(traffic.Total));
    }
}
