using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Controls;

public sealed partial class TrafficBottomSheet : UserControl
{
    public event Action? UpgradeTrafficRequested;

    public TrafficBottomSheet()
    {
        InitializeComponent();
        IncreaseButton.Content = Strings.AccountIncreaseLimits;
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
            SetBusy(Strings.Loading);
            return;
        }
        if (traffic is null)
        {
            SetBusy(Strings.AccountTrafficUnavailable);
            return;
        }

        var remaining = Math.Max(0L, traffic.LimitBytes - traffic.Total);
        TitleText.Text = string.Format(Strings.AccountTrafficRemaining, VpnAccountApi.FormatBytes(remaining));
        TitleText.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        Pie.Visibility = Visibility.Visible;
        IncreaseButton.Visibility = Visibility.Visible;
        Pie.Bind(
            traffic.Servers,
            Strings.AccountTrafficOther,
            VpnAccountApi.FormatBytes(traffic.Total),
            AuthRepository.Session?.UserId ?? "");
    }

    private void SetBusy(string status)
    {
        TitleText.Visibility = Visibility.Collapsed;
        StatusText.Text = status;
        StatusText.Visibility = Visibility.Visible;
        Pie.Visibility = Visibility.Collapsed;
        IncreaseButton.Visibility = Visibility.Collapsed;
    }

    private void OnIncreaseLimits(object sender, RoutedEventArgs e)
    {
        Dismiss();
        UpgradeTrafficRequested?.Invoke();
    }
}
