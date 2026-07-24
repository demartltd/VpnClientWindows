using DemarVpn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Views;

public sealed partial class AccountPage : Page
{
    public event Action? SignOutRequested;

    public string Title { get; } = Strings.AccountTitle;
    public string SignOutLabel { get; } = Strings.SignOut;

    public AccountPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var u = AppServices.Auth.User;
        if (u is null)
        {
            UserName.Text = Strings.AccountSignInHint;
            UserId.Text = string.Empty;
            UserVpn.Text = string.Empty;
            UserExpires.Text = string.Empty;
            return;
        }
        UserName.Text = u.Username;
        UserId.Text = u.Id;
        UserVpn.Text = $"VPN: {u.VpnSubscription}";
        UserExpires.Text = u.VpnExpiresAt is not null ? $"До {u.VpnExpiresAt}" : string.Empty;
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        SignOutRequested?.Invoke();
    }
}
