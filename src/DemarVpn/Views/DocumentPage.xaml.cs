using DemarVpn.Domain.Content;
using DemarVpn.Services.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DemarVpn.Views;

public sealed partial class DocumentPage : OverlayPage
{
    private CancellationTokenSource? _cts;
    private LegalDocument? _doc;

    public DocumentPage()
    {
        InitializeComponent();
        ErrorText.Text = Strings.DocumentLoadFailed;
        RetryButton.Content = Strings.ServersRetry;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not LegalDocument doc)
        {
            GoBack();
            return;
        }
        _doc = doc;
        Chrome.Title = doc.Title();
        StartLoad();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        CancelLoad();
        base.OnNavigatedFrom(e);
    }

    internal override void PrepareClose() => CancelLoad();

    private void CancelLoad()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void StartLoad()
    {
        CancelLoad();
        _cts = new CancellationTokenSource();
        _ = LoadAsync(_cts.Token);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (_doc is not { } doc) return;
        ShowLoading();
        try
        {
            var md = await LegalDocumentRepository.LoadAsync(doc, ct);
            if (ct.IsCancellationRequested) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ct.IsCancellationRequested) return;
                MarkdownBlocks.Fill(BlocksHost, md);
                ShowContent();
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLog.Append($"DocumentPage load {doc} failed: {ex.Message}");
            if (!ct.IsCancellationRequested)
                DispatcherQueue.TryEnqueue(ShowError);
        }
    }

    private void ShowLoading()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
    }

    private void ShowContent()
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;
    }

    private void ShowError()
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void OnRetry(object sender, RoutedEventArgs e) => StartLoad();
}
