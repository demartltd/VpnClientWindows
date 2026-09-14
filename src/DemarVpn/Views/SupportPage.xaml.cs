using DemarVpn.Controls;
using DemarVpn.Services;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Support;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DemarVpn.Views;

public sealed partial class SupportPage : OverlayPage
{
    private SupportRoute? _route;
    private List<SupportTicketSummary> _tickets = [];
    private List<SupportTheme> _themes = [];
    private CancellationTokenSource? _listPoll;
    private bool _loadingList = true;

    public SupportPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!CreateShadow.Receivers.Contains(ShadowCast))
                CreateShadow.Receivers.Add(ShadowCast);
        };
        Unloaded += (_, _) => StopPolling();
        ChatPane.Error += OnChatError;
        ChatPane.TicketGone += () =>
        {
            if (_route is SupportRoute.Chat chat)
                SupportStore.RemoveTicket(AuthSessionStore.Current?.UserId ?? "", chat.TicketId);
            _ = ReloadListAsync(quiet: true);
            SetRoute(null);
        };
        ChatPane.TicketCreated += t =>
        {
            _ = ReloadListAsync(quiet: true);
            SetRoute(new SupportRoute.Chat(t.Id));
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is SupportRoute r) _route = r;
        var session = AuthSessionStore.Current;
        if (session is null) { GoBack(); return; }

        StopPolling();
        _tickets = SupportStore.LoadList(session.UserId)?.ToList() ?? [];
        SupportStore.Apply(_tickets);
        if (_tickets.Count > 0) _loadingList = false;
        RenderList();
        ApplyRoute();
        _ = EnsureThemesAsync();
        _ = ReloadListAsync(quiet: _tickets.Count > 0);
        _listPoll = new CancellationTokenSource();
        _ = ListPollAsync(_listPoll.Token);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopPolling();
    }

    internal override void PrepareClose() => StopPolling();

    private void StopPolling()
    {
        _listPoll?.Cancel();
        _listPoll = null;
        ChatPane.Stop();
    }

    protected override bool OnOverlayBack()
    {
        if (_route is null) return false;
        _ = ReloadListAsync(quiet: true);
        SetRoute(null);
        return true;
    }

    private async Task ListPollAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(8_000, ct);
                if (_route is not null) continue;
                await ReloadListAsync(quiet: true);
            }
        }
        catch (OperationCanceledException) { /* stop */ }
    }

    private async Task ReloadListAsync(bool quiet)
    {
        var session = AuthSessionStore.Current;
        if (session is null) return;
        if (_tickets.Count == 0) SetListLoading(true);
        try
        {
            var fresh = await SupportApi.ListTicketsAsync(session.UserId, session.LoginAt);
            _tickets = fresh.ToList();
            SupportStore.SaveList(session.UserId, fresh);
            SupportStore.Apply(fresh);
            RenderList();
        }
        catch (Exception ex)
        {
            if (_tickets.Count == 0 || !quiet)
            {
                var msg = SupportApi.SupportErrorMessage(ex);
                if (msg is not null) AppServices.Snackbar.ShowError(msg);
            }
        }
        finally { SetListLoading(false); }
    }

    private async Task EnsureThemesAsync()
    {
        if (_themes.Count > 0) return;
        var session = AuthSessionStore.Current;
        if (session is null) return;
        try
        {
            _themes = (await SupportApi.ListThemesAsync(session.UserId, session.LoginAt)).ToList();
        }
        catch (Exception ex)
        {
            var msg = SupportApi.SupportErrorMessage(ex);
            if (msg is not null) AppServices.Snackbar.ShowError(msg);
        }
    }

    private void SetListLoading(bool on)
    {
        _loadingList = on;
        ListLoading.IsActive = on && _tickets.Count == 0;
        ListLoading.Visibility = ListLoading.IsActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderList()
    {
        ListHost.Children.Clear();
        EmptyBlock.Visibility = !_loadingList && _tickets.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        EmptyBlock.Text = Strings.SupportEmpty;

        foreach (var t in _tickets)
        {
            var status = t.IsOpen ? Strings.SupportStatusOpen : Strings.SupportStatusClosed;
            var preview = t.Preview?.Split('\n').FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(preview)) preview = Strings.SupportNoPreview;
            var row = new SettingsActionButton
            {
                Glyph = "\uE939",
                Title = $"{status} · #{t.Id}",
                Subtitle = preview,
                ShowUnread = t.HasUnread,
            };
            var id = t.Id;
            row.Click += (_, _) => SetRoute(new SupportRoute.Chat(id));
            ListHost.Children.Add(row);
        }
    }

    private void SetRoute(SupportRoute? route)
    {
        _route = route;
        ApplyRoute();
    }

    private void ApplyRoute()
    {
        switch (_route)
        {
            case SupportRoute.Draft d:
                Chrome.Title = Strings.SupportCreate;
                ListLayer.Visibility = Visibility.Collapsed;
                ChatPane.Visibility = Visibility.Visible;
                ChatPane.Start(null, d.ThemeId);
                break;
            case SupportRoute.Chat c:
                Chrome.Title = string.Format(Strings.SupportTicket, c.TicketId);
                ListLayer.Visibility = Visibility.Collapsed;
                ChatPane.Visibility = Visibility.Visible;
                ChatPane.Start(c.TicketId);
                break;
            default:
                Chrome.Title = Strings.SupportTitle;
                ChatPane.Stop();
                ChatPane.Visibility = Visibility.Collapsed;
                ListLayer.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        var openLocal = _tickets.Any(t => t.IsOpen && t.Channel == SupportApi.Channel);
        if (openLocal)
        {
            if (!await DemarDialog.ConfirmAsync(
                    XamlRoot,
                    Strings.SupportOpenExistsTitle,
                    Strings.SupportOpenExistsBody,
                    Strings.SupportCreateNew,
                    Strings.Cancel))
                return;
        }
        await OpenThemePickerAsync();
    }

    private async Task OpenThemePickerAsync()
    {
        await EnsureThemesAsync();
        if (_themes.Count == 0)
        {
            AppServices.Snackbar.ShowError(Strings.SupportThemesEmpty);
            return;
        }

        SupportTheme? selected = _themes.Count == 1 ? _themes[0] : null;
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = Strings.SupportPickTheme,
            ItemsSource = _themes.Select(t => t.Name).ToList(),
        };
        if (selected is not null) combo.SelectedIndex = 0;

        if (XamlRoot is null) return;
        var dialog = DemarDialog.Create(XamlRoot, Strings.SupportThemeSub, combo);
        dialog.PrimaryButtonText = Strings.SupportCreate;
        dialog.CloseButtonText = Strings.Cancel;
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.IsPrimaryButtonEnabled = selected is not null;
        combo.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = combo.SelectedIndex >= 0;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var idx = combo.SelectedIndex;
        if (idx < 0 || idx >= _themes.Count) return;
        SetRoute(new SupportRoute.Draft(_themes[idx].Id));
    }

    private void OnChatError(Exception ex)
    {
        var msg = SupportApi.SupportErrorMessage(ex);
        if (msg is not null) AppServices.Snackbar.ShowError(msg);
    }
}
