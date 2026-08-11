using DemarVpn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DemarVpn.Views;

public sealed partial class AppSettingsPage : OverlayPage
{
    private bool _expanded;
    private bool _betaUiReady;

    public AppSettingsPage()
    {
        InitializeComponent();
        Chrome.Title = Strings.AppSettingsTitle;
        BetaTitle.Text = Strings.SettingsBeta;
        BetaSub.Text = Strings.SettingsBetaSub;
        BetaToggle.IsOn = UpdateChannelStore.PreferBeta;
        _betaUiReady = true;
        AppLog.Changed += () => DispatcherQueue.TryEnqueue(RefreshJournal);
        RefreshJournal();
        UpdateExpandLabel();
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

        // Revert until user confirms leave-beta dialog.
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
