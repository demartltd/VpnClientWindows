using DemarVpn.Services;
using DemarVpn.Services.Account;
using DemarVpn.Services.Auth;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DemarVpn.Controls;

public sealed partial class NotifyBottomSheet : UserControl
{
    private bool _busy;
    private bool _suppress;

    public NotifyBottomSheet()
    {
        InitializeComponent();
        NewsLabel.Text = Strings.AccountNotifyNews;
        ImportantLabel.Text = Strings.AccountNotifyImportant;
        LoadingText.Text = Strings.Loading;
    }

    public void Show()
    {
        BindFromStore();
        Visibility = Visibility.Visible;
        SheetSlide.Y = 320;
        var anim = new DoubleAnimation
        {
            From = 320,
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
        var account = AccountStore.Account;
        var loading = AccountStore.Loading;
        if (account is null && loading)
        {
            LoadingText.Visibility = Visibility.Visible;
            RowsHost.Visibility = Visibility.Collapsed;
            return;
        }
        LoadingText.Visibility = Visibility.Collapsed;
        if (account is null)
        {
            RowsHost.Visibility = Visibility.Collapsed;
            return;
        }
        RowsHost.Visibility = Visibility.Visible;
        _suppress = true;
        NewsSwitch.IsOn = account.NotifyNews;
        ImportantSwitch.IsOn = account.NotifyImportant;
        NewsSwitch.IsEnabled = !_busy;
        ImportantSwitch.IsEnabled = !_busy;
        _suppress = false;
    }

    private void OnScrimPressed(object sender, PointerRoutedEventArgs e) => Dismiss();

    private async void OnNewsToggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        await SetNotifyAsync(NewsSwitch.IsOn, null);
    }

    private async void OnImportantToggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        await SetNotifyAsync(null, ImportantSwitch.IsOn);
    }

    private async Task SetNotifyAsync(bool? news, bool? important)
    {
        var cur = AccountStore.Account;
        var session = AuthSessionStore.Current;
        if (cur is null || session is null || _busy) return;

        var next = cur with
        {
            NotifyNews = news ?? cur.NotifyNews,
            NotifyImportant = important ?? cur.NotifyImportant,
        };
        AccountStore.ApplyAccount(next);
        _busy = true;
        BindFromStore();
        try
        {
            var updated = await VpnAccountApi.UpdateNotifyAsync(
                session.UserId, session.LoginAt, news, important);
            AccountStore.ApplyAccount(next with
            {
                NotifyNews = updated.NotifyNews,
                NotifyImportant = updated.NotifyImportant,
            });
        }
        catch (AuthException e)
        {
            AccountStore.ApplyAccount(cur);
            if (e.IsSessionInvalid)
            {
                AuthRepository.ClearLocal();
                AppServices.Auth.NotifyChanged();
                Dismiss();
            }
            else
                AppServices.Snackbar.ShowError(Strings.AccountNotifyFailed);
        }
        catch
        {
            AccountStore.ApplyAccount(cur);
            AppServices.Snackbar.ShowError(Strings.AccountNotifyFailed);
        }
        finally
        {
            _busy = false;
            BindFromStore();
        }
    }
}
