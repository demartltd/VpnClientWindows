using DemarVpn.Controls;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DemarVpn.Views;

public sealed partial class SettingsPage : Page
{
    public event Action? AppSettingsRequested;
    public event Action? ServiceMenuRequested;
    public event Action? DocumentsRequested;
    public event Action? SignInRequested;

    private int _tapCount;
    private DateTimeOffset _tapWindow;

    public SettingsPage()
    {
        InitializeComponent();
        VersionLine.Text = $"Demar VPN - {Strings.VersionName} - {Strings.PlatformWindows}";
        SingBoxLine.Text = $"sing-box - {AppServices.SingBoxLabel}";
        BuildSocialLinks();
        BuildMenu();
    }

    private void BuildSocialLinks()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (icon, url) in SocialLinks.All)
            panel.Children.Add(new IconLinkChip { Icon = icon, Url = url });
        SocialLinksHost.Content = panel;
    }

    private void BuildMenu()
    {
        AddRow("\uE713", Strings.SettingsApp, Strings.SettingsAppSub,
            (_, _) => AppSettingsRequested?.Invoke());
        AddRow("\uE939", Strings.SettingsFeedback, Strings.SettingsFeedbackSub,
            (_, _) => AppServices.Snackbar.ShowSoon());
        AddRow("\uE72D", Strings.SettingsShare, Strings.SettingsShareSub, OnShare);
        AddRow("\uE8EC", Strings.SettingsPromo, Strings.SettingsPromoSub, OnPromo);
        AddRow("\uE8B7", Strings.SettingsDocuments, Strings.SettingsDocumentsSub,
            (_, _) => DocumentsRequested?.Invoke());
    }

    private void AddRow(string glyph, string title, string subtitle, EventHandler click)
    {
        var row = new SettingsActionButton { Glyph = glyph, Title = title, Subtitle = subtitle };
        row.Click += click;
        MenuHost.Children.Add(row);
    }

    private void OnShare(object? sender, EventArgs e)
    {
        var data = new DataPackage();
        data.SetText(Strings.ShareAppPitch);
        Clipboard.SetContent(data);
        AppServices.Snackbar.ShowInfo("Text copied to clipboard");
    }

    private async void OnPromo(object? sender, EventArgs e)
    {
        if (!AppServices.Auth.SignedIn)
        {
            SignInRequested?.Invoke();
            return;
        }
        if (XamlRoot is null) return;

        var input = new TextBox
        {
            PlaceholderText = Strings.PromoPlaceholder,
            CharacterCasing = CharacterCasing.Upper,
        };
        input.TextChanged += (_, _) =>
        {
            var filtered = FilterPromoCode(input.Text);
            if (filtered != input.Text)
            {
                var sel = input.SelectionStart;
                input.Text = filtered;
                input.SelectionStart = Math.Min(sel, filtered.Length);
            }
        };

        var dialog = new ContentDialog
        {
            Title = Strings.PromoEnterTitle,
            Content = input,
            PrimaryButtonText = Strings.PromoActivate,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var code = input.Text.Trim();
        if (code.Length == 0) return;

        var session = AuthSessionStore.Current;
        if (session is null) return;

        try
        {
            var result = await VpnAccountApi.ActivatePromoAsync(session.UserId, session.LoginAt, code);
            if (result.IsActivated)
                await AppServices.Vpn.RefreshServersAsync();
            if (result.IsActivated)
                AppServices.Snackbar.ShowSuccess(result.LocalizedMessage);
            else
                AppServices.Snackbar.ShowError(result.LocalizedMessage);
        }
        catch (Exception ex)
        {
            AppServices.Snackbar.ShowError(ex.Message);
        }
    }

    private static string FilterPromoCode(string raw) =>
        string.Concat(raw.ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'));

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
