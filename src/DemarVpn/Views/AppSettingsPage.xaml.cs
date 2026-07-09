using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DemarVpn.Views;

public sealed partial class AppSettingsPage : OverlayPage
{
    private bool _expanded;

    public AppSettingsPage()
    {
        InitializeComponent();
        AppLog.Changed += () => DispatcherQueue.TryEnqueue(RefreshJournal);
        RefreshJournal();
        UpdateExpandLabel();
    }

    private void OnBack(object sender, RoutedEventArgs e) => GoBack();

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
        ExpandBtn.Content = Strings.JournalExpand + (_expanded || count == 0 ? $" ({count})" : $" ({count})");
    }

    private void RefreshJournal()
    {
        var lines = AppLog.Snapshot();
        JournalText.Text = lines.Count == 0 ? Strings.JournalEmpty : string.Join(Environment.NewLine, lines.Reverse());
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
        AppServices.Snackbar.Show(Models.SnackKind.Success, "Journal copied to clipboard");
    }
}
