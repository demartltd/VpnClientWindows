using DemarVpn.Domain.Content;

namespace DemarVpn.Services.Content;

public static class LegalDocumentRepository
{
    private static readonly Dictionary<LegalDocument, string> Cache = new();

    public static async Task<string> LoadAsync(LegalDocument document, CancellationToken ct = default)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(document, out var hit)) return hit;
        }

        var md = await RawTextFetcher.GetAsync(document.Url(), ct);
        lock (Cache) Cache[document] = md;
        return md;
    }

    /// <summary>Visible for tests.</summary>
    public static void ClearCache()
    {
        lock (Cache) Cache.Clear();
    }

    /// <summary>Visible for tests.</summary>
    public static void SeedCache(LegalDocument document, string md)
    {
        lock (Cache) Cache[document] = md;
    }
}
