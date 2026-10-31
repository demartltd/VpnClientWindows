using System.Text;
using System.Text.Json.Nodes;
using DemarVpn.Services;
using DemarVpn.Services.Auth;

namespace DemarVpn.Tests;

public class AppUpdateTest
{
    [Fact]
    public void IsNewer_comparesSemver()
    {
        Assert.True(WindowsReleaseApi.IsNewer("1.0.10", "1.0.9"));
        Assert.False(WindowsReleaseApi.IsNewer("1.0.9", "1.0.9"));
        Assert.False(WindowsReleaseApi.IsNewer("1.0.8", "1.0.9"));
        Assert.True(WindowsReleaseApi.IsNewer("v1.1.0", "1.0.9"));
        Assert.False(WindowsReleaseApi.IsNewer("not-a-version", "1.0.9"));
        Assert.True(WindowsReleaseApi.IsNewer("1.0.0", "bogus"));
    }

    [Fact]
    public void TryParseVersion_stripsPrefix()
    {
        Assert.True(WindowsReleaseApi.TryParseVersion("v1.0.9", out var v));
        Assert.Equal(new Version(1, 0, 9), v);
        Assert.False(WindowsReleaseApi.TryParseVersion("", out _));
        Assert.False(WindowsReleaseApi.TryParseVersion("1.x.0", out _));
    }

    [Fact]
    public void Sha256Hex_knownVector()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            WindowsReleaseApi.Sha256Hex(Encoding.UTF8.GetBytes("abc")));
    }

    [Fact]
    public void Parse_stableWithoutAsset()
    {
        var json = JsonNode.Parse(
            """{"version":"1.0.12","sha256":"abc","publishedAt":"2026-01-01","beta":null}""")!.AsObject();
        var release = WindowsReleaseApi.Parse(json, preferBeta: false)!;
        Assert.Equal("1.0.12", release.Version);
        Assert.Equal("Updater.exe", release.Asset);
        Assert.Equal(WindowsReleaseApi.DownloadUrl, release.DownloadUrl);
        Assert.StartsWith(DemarHttp.VpnApiBaseUrl.TrimEnd('/') + "/", release.DownloadUrl);
    }

    [Fact]
    public void Parse_preferBetaPicksNewerBeta()
    {
        var json = JsonNode.Parse(
            """
            {
              "version":"1.0.12",
              "sha256":"aaa",
              "beta":{"version":"1.0.13","sha256":"bbb","publishedAt":"2026-01-02"}
            }
            """)!.AsObject();
        var release = WindowsReleaseApi.Parse(json, preferBeta: true)!;
        Assert.Equal("1.0.13", release.Version);
        Assert.Equal("bbb", release.Sha256);
        Assert.Equal(WindowsReleaseApi.BetaUrl, release.DownloadUrl);
    }

    [Fact]
    public void Parse_preferBetaPicksNewerStableOverBeta()
    {
        var json = JsonNode.Parse(
            """
            {
              "version":"1.0.14",
              "sha256":"aaa",
              "beta":{"version":"1.0.13","sha256":"bbb"}
            }
            """)!.AsObject();
        var release = WindowsReleaseApi.Parse(json, preferBeta: true)!;
        Assert.Equal("1.0.14", release.Version);
        Assert.Equal("aaa", release.Sha256);
        Assert.Equal(WindowsReleaseApi.DownloadUrl, release.DownloadUrl);
    }

    [Fact]
    public void Parse_preferBetaFallsBackWhenNull()
    {
        var json = JsonNode.Parse(
            """{"version":"1.0.12","sha256":"abc","beta":null}""")!.AsObject();
        var release = WindowsReleaseApi.Parse(json, preferBeta: true)!;
        Assert.Equal("1.0.12", release.Version);
        Assert.Equal(WindowsReleaseApi.DownloadUrl, release.DownloadUrl);
    }

    [Fact]
    public void Parse_ignoresBetaWhenDisabled()
    {
        var json = JsonNode.Parse(
            """
            {
              "version":"1.0.12",
              "sha256":"aaa",
              "asset":"custom.exe",
              "beta":{"version":"1.0.13","sha256":"bbb"}
            }
            """)!.AsObject();
        var release = WindowsReleaseApi.Parse(json, preferBeta: false)!;
        Assert.Equal("1.0.12", release.Version);
        Assert.Equal("custom.exe", release.Asset);
        Assert.Equal(WindowsReleaseApi.DownloadUrl, release.DownloadUrl);
    }

    [Fact]
    public void Parse_requiresSha256()
    {
        var json = JsonNode.Parse("""{"version":"1.0.12"}""")!.AsObject();
        Assert.Null(WindowsReleaseApi.Parse(json, preferBeta: false));
    }
}
