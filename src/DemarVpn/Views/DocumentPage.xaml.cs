using DemarVpn.Domain.Content;
using DemarVpn.Services.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DemarVpn.Views;

public sealed partial class DocumentPage : OverlayPage
{
    private CancellationTokenSource? _cts;

    public DocumentPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not LegalDocument doc)
        {
            GoBack();
            return;
        }
        TitleBlock.Text = doc.Title();
        _cts = new CancellationTokenSource();
        _ = LoadLoopAsync(doc, _cts.Token);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnNavigatedFrom(e);
    }

    private async Task LoadLoopAsync(LegalDocument doc, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ShowLoading();
            try
            {
                var md = await LegalDocumentRepository.LoadAsync(doc, ct);
                if (ct.IsCancellationRequested) return;
                MarkdownBlocks.Fill(BlocksHost, md);
                ShowContent();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLog.Append($"DocumentPage load {doc} failed: {ex.Message}");
            }
            try { await Task.Delay(2000, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void ShowLoading()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
    }

    private void ShowContent()
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;
    }

    private void OnBack(object sender, RoutedEventArgs e) => GoBack();
}
