using DemarVpn.Controls;
using DemarVpn.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DemarVpn.Views;

public sealed partial class AccountPage : Page
{
    private bool _signedIn;

    public AccountPage()
    {
        InitializeComponent();
        Refresh();
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _signedIn = true;
            Refresh();
        }
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _signedIn = false;
        Refresh();
    }

    private void Refresh()
    {
        SignedOutPanel.Visibility = _signedIn ? Visibility.Collapsed : Visibility.Visible;
        SignedInPanel.Visibility = _signedIn ? Visibility.Visible : Visibility.Collapsed;
        if (!_signedIn) return;
        var u = UiMocks.SignedInUser;
        UserName.Text = u.Username;
        UserId.Text = u.Id;
        UserVpn.Text = $"VPN: {u.VpnSubscription}";
        UserExpires.Text = u.VpnExpiresAt is not null ? $"До {u.VpnExpiresAt}" : string.Empty;
    }
}

internal sealed class LoginDialog : ContentDialog
{
    private enum Step { Email, Otp, Totp }

    private readonly TextBox _input = new() { PlaceholderText = Strings.LoginEmailHint };
    private readonly TextBlock _title = new();
    private Step _step = Step.Email;
    private string _email = string.Empty;

    public LoginDialog()
    {
        Title = Strings.LoginSheetTitle;
        PrimaryButtonText = Strings.LoginContinue;
        CloseButtonText = "Отмена";
        Content = BuildContent();
        PrimaryButtonClick += OnPrimary;
    }

    private StackPanel BuildContent()
    {
        _title.TextWrapping = TextWrapping.Wrap;
        _title.Margin = new Thickness(0, 0, 0, 8);
        var tg = new IconLinkChip { IconKey = "BrandIcon_telegram", Url = "https://t.me/demargames" };
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                _title,
                _input,
                new TextBlock { Text = Strings.LoginMoreMethods, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center },
                tg,
            },
        };
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        switch (_step)
        {
            case Step.Email:
                _email = _input.Text.Trim();
                _step = Step.Otp;
                _title.Text = string.Format(Strings.LoginOtpSent, _email);
                _input.Text = string.Empty;
                _input.PlaceholderText = "000000";
                args.Cancel = true;
                break;
            case Step.Otp:
                if (_input.Text.Length < 6) { args.Cancel = true; return; }
                _step = Step.Totp;
                _title.Text = Strings.LoginTotp;
                _input.Text = string.Empty;
                args.Cancel = true;
                break;
            case Step.Totp:
                if (_input.Text.Length < 6) args.Cancel = true;
                break;
        }
    }
}
