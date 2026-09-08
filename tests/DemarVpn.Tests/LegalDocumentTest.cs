using System.Text;
using DemarVpn.Domain.Content;
using DemarVpn.Services.Content;

namespace DemarVpn.Tests;

public class LegalDocumentTest
{
    [Fact]
    public void Urls_PointToTosPpRepo()
    {
        Assert.Equal(
            "https://raw.githubusercontent.com/demartltd/tos-pp/refs/heads/main/ToS.md",
            LegalDocument.Terms.Url());
        Assert.Equal(
            "https://raw.githubusercontent.com/demartltd/tos-pp/refs/heads/main/pp.md",
            LegalDocument.Privacy.Url());
        Assert.All(LegalDocuments.All, d => Assert.StartsWith("https://", d.Url()));
    }

    [Fact]
    public void Decode_AcceptsUtf8Body()
    {
        var bytes = Encoding.UTF8.GetBytes("# Terms\n\nHello");
        Assert.Equal("# Terms\n\nHello", RawTextFetcher.Decode(200, bytes));
    }

    [Fact]
    public void Decode_RejectsNon200()
    {
        var ex = Assert.Throws<IOException>(() => RawTextFetcher.Decode(404, Encoding.UTF8.GetBytes("# x")));
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public void Decode_RejectsEmpty()
    {
        Assert.Throws<IOException>(() => RawTextFetcher.Decode(200, Encoding.UTF8.GetBytes("   \n")));
    }

    [Fact]
    public void Decode_RejectsInvalidUtf8()
    {
        Assert.Throws<IOException>(() => RawTextFetcher.Decode(200, [0xFF, 0xFE]));
    }

    [Fact]
    public async Task Repository_CacheHit_SkipsNetwork()
    {
        LegalDocumentRepository.ClearCache();
        LegalDocumentRepository.SeedCache(LegalDocument.Terms, "# cached");
        Assert.Equal("# cached", await LegalDocumentRepository.LoadAsync(LegalDocument.Terms));
        LegalDocumentRepository.ClearCache();
    }
}
