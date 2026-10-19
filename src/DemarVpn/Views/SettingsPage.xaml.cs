using DemarVpn.Controls;
using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Support;
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
    public event Action? SupportRequested;

    private int _tapCount;
    private DateTimeOffset _tapWindow;
    private SettingsActionButton? _supportRow;

    public SettingsPage()
    {
        InitializeComponent();
        HeaderSub.Text = Strings.SettingsHeaderSubtitle;
        VersionLine.Text = $"Demar VPN - {Strings.VersionName} - {Strings.PlatformWindows}";
        SingBoxLine.Text = $"sing-box - {AppServices.SingBoxLabel}";
        BuildSocialLinks();
        BuildMenu();
        SupportStore.Changed += OnSupportUnreadChanged;
        Unloaded += (_, _) => SupportStore.Changed -= OnSupportUnreadChanged;
        OnSupportUnreadChanged();
    }

    private void OnSupportUnreadChanged() =>
        Ui.Run(DispatcherQueue, () =>
        {
            if (_supportRow is not null)
                _supportRow.ShowUnread = SupportStore.HasUnread;
        });

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
        _supportRow = AddRow("\uE939", Strings.SettingsFeedback, Strings.SettingsFeedbackSub, OnSupport);
        AddRow("\uE72D", Strings.SettingsShare, Strings.SettingsShareSub, OnShare);
        AddRow("\uE8EC", Strings.SettingsPromo, Strings.SettingsPromoSub, OnPromo);
        AddRow("\uE8B7", Strings.SettingsDocuments, Strings.SettingsDocumentsSub,
            (_, _) => DocumentsRequested?.Invoke());
    }

    private SettingsActionButton AddRow(string glyph, string title, string subtitle, EventHandler click)
    {
        var row = new SettingsActionButton { Glyph = glyph, Title = title, Subtitle = subtitle };
        row.Click += click;
        MenuHost.Children.Add(row);
        return row;
    }

    private void OnSupport(object? sender, EventArgs e) => SupportRequested?.Invoke();

    private void OnShare(object? sender, EventArgs e)
    {
        var data = new DataPackage();
        data.SetText(Strings.ShareAppPitch);
        Clipboard.SetContent(data);
        AppServices.Snackbar.ShowInfo(Strings.ShareCopied);
    }

    private async void OnPromo(object? sender, EventArgs e)
    {
        if (!AppServices.Auth.SignedIn)
        {
            SignInRequested?.Invoke();
            return;
        }
        if (XamlRoot is null) return;

        var code = await DemarDialog.PromptPromoCodeAsync(
            XamlRoot, Strings.PromoActivate, Strings.PromoPlaceholder);
        if (code is null) return;

        var session = AuthSessionStore.Current;
        if (session is null) return;

        try
        {
            var result = await Ui.ApiAsync(DispatcherQueue,
                () => VpnAccountApi.ActivatePromoAsync(session.UserId, session.LoginAt, code));
            if (result.IsActivated)
            {
                await Ui.ApiAsync(DispatcherQueue, () => AppServices.Vpn.RefreshServersAsync());
                AppServices.Snackbar.ShowSuccess(result.LocalizedMessage);
            }
            else
                AppServices.Snackbar.ShowError(result.LocalizedMessage);
        }
        catch (Exception ex)
        {
            AppServices.Snackbar.ShowError(ex.Message);
        }
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
