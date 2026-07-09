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
        AddRow("\uE713", "Настройки приложения", "Подключение, журнал диагностики и статус функций",
            (_, _) => AppSettingsRequested?.Invoke());
        AddRow("\uE939", "Обратная связь", "Отправить ошибку или предложение",
            (_, _) => AppServices.Snackbar.ShowSoon());
        AddRow("\uE72D", "Пригласите друзей по ссылке", "Получите бесплатный премиум", OnShare);
        AddRow("\uE8A5", "Условия использования", "Правила использования сервиса",
            (_, _) => AppServices.Snackbar.ShowSoon());
        AddRow("\uF5B4", "Конфиденциальность", "Как обрабатываются данные",
            (_, _) => AppServices.Snackbar.ShowSoon());
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
