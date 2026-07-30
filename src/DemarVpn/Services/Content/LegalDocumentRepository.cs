using DemarVpn.Domain.Content;

namespace DemarVpn.Services.Content;

public static class LegalDocumentRepository
{
    public static Task<string> LoadAsync(LegalDocument document, CancellationToken ct = default) =>
        RawTextFetcher.GetAsync(document.Url(), ct);
}
