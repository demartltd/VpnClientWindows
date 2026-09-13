using DemarVpn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DemarVpn.Views;

public sealed partial class AppSettingsPage : OverlayPage
{
    private bool _expanded;
    private bool _startupUiReady;
    private bool _betaUiReady;

    public AppSettingsPage()
    {
        InitializeComponent();
        Chrome.Title = Strings.AppSettingsTitle;
        LanguageRow.Title = Strings.SettingsLanguage;
        LanguageRow.Subtitle = LocaleStore.DisplayName;
        TunRow.Title = Strings.SettingsTun;
        TunRow.Subtitle = Strings.SettingsTunSub;
        StartupTitle.Text = Strings.SettingsRunAtStartup;
        StartupSub.Text = Strings.SettingsRunAtStartupSub;
        StartupToggle.IsOn = RunAtStartup.Enabled;
        _startupUiReady = true;
        BetaTitle.Text = Strings.SettingsBeta;
        BetaSub.Text = Strings.SettingsBetaSub;
        BetaToggle.IsOn = UpdateChannelStore.PreferBeta;
        _betaUiReady = true;
        JournalTitle.Text = Strings.JournalTitle;
        JournalSub.Text = Strings.JournalSubtitle;
        JournalEmpty.Text = Strings.JournalEmpty;
        ClearBtn.Content = Strings.JournalClear;
        ShareBtn.Content = Strings.JournalShare;
        AppLog.Changed += () => DispatcherQueue.TryEnqueue(RefreshJournal);
        RefreshJournal();
        UpdateExpandLabel();
    }

    private async void OnLanguage(object? sender, EventArgs e)
    {
        if (XamlRoot is null) return;
        var en = new RadioButton { Content = "English", IsChecked = !LocaleStore.IsRu, GroupName = "lang" };
        var ru = new RadioButton { Content = "Русский", IsChecked = LocaleStore.IsRu, GroupName = "lang" };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(en);
        panel.Children.Add(ru);
        var dialog = new ContentDialog
        {
            Title = Strings.SettingsLanguage,
            Content = panel,
            PrimaryButtonText = Strings.Ok,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var tag = ru.IsChecked == true ? LocaleStore.Ru : LocaleStore.En;
        if (tag == LocaleStore.Tag) return;
        LocaleStore.Set(tag);
        App.RemountShell(openAppSettings: true);
    }

    private void OnTunSettings(object? sender, EventArgs e) => Frame?.Navigate(typeof(TunSettingsPage));

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_startupUiReady) return;
        RunAtStartup.Set(StartupToggle.IsOn);
    }

    private async void OnBetaToggled(object sender, RoutedEventArgs e)
    {
        if (!_betaUiReady) return;
        var on = BetaToggle.IsOn;
        if (on)
        {
            ApplyChannel(true);
            return;
        }

        _betaUiReady = false;
        BetaToggle.IsOn = true;
        _betaUiReady = true;

        if (XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title = Strings.SettingsBetaOffTitle,
            Content = Strings.SettingsBetaOffBody,
            PrimaryButtonText = Strings.SettingsBetaOffOk,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
        _betaUiReady = false;
        BetaToggle.IsOn = false;
        _betaUiReady = true;
        ApplyChannel(false);
    }

    private void ApplyChannel(bool preferBeta)
    {
        UpdateChannelStore.Set(preferBeta);
        AppServices.Update.ClearPendingUpdate();
        _ = AppServices.Update.CheckAndDownloadAsync();
    }

    private void OnExpand(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        JournalBox.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        UpdateExpandLabel();
        if (_expanded) RefreshJournal();
    }

    private void UpdateExpandLabel()
    {
        var count = AppLog.Snapshot().Count;
        ExpandBtn.Content = count == 0 ? Strings.JournalExpand : $"{Strings.JournalExpand} ({count})";
    }

    private void RefreshJournal()
    {
        var lines = AppLog.Snapshot();
        var empty = lines.Count == 0;
        JournalEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        JournalScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        JournalText.Text = empty ? string.Empty : string.Join(Environment.NewLine, lines.Reverse());
        UpdateExpandLabel();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        AppLog.Clear();
        AppServices.Snackbar.Show(Models.SnackKind.Success, Strings.JournalCleared);
        RefreshJournal();
    }

    private void OnShare(object sender, RoutedEventArgs e)
    {
        var body = AppLog.ReadPersistedUtf8().Trim();
        if (string.IsNullOrEmpty(body))
        {
            AppServices.Snackbar.Show(Models.SnackKind.Info, Strings.JournalShareEmpty);
            return;
        }
        var data = new DataPackage();
        data.SetText(body);
        Clipboard.SetContent(data);
        AppServices.Snackbar.Show(Models.SnackKind.Success, Strings.JournalShareCopied);
    }
}
