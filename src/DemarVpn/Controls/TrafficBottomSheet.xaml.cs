using DemarVpn.Services.Account;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Controls;

public sealed partial class TrafficBottomSheet : UserControl
{
    public TrafficBottomSheet() => InitializeComponent();

    public void Show()
    {
        BindFromStore();
        Visibility = Visibility.Visible;
        SheetSlide.Y = 420;
        var anim = new DoubleAnimation
        {
            From = 420,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, SheetSlide);
        Storyboard.SetTargetProperty(anim, "Y");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    public void Dismiss() => Visibility = Visibility.Collapsed;

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

    private void OnScrimPressed(object sender, PointerRoutedEventArgs e) => Dismiss();
}
