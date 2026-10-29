using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services.Vpn;

namespace DemarVpn.Tests;

public class TunGroupsParseTest
{
    private const string SampleJson =
        """
        {
          "windows": [
            {
              "name": "Games",
              "apps": [
                {"name": "Steam", "id": "steam.exe"},
                {"name": "Epic", "id": "EpicGamesLauncher.exe"}
              ],
              "enabledByDefault": true,
              "isWhiteList": true
            },
            {
              "name": "RU Banks",
              "apps": [{"name": "Сбер", "id": "sber.exe"}],
              "enabledByDefault": false,
              "isWhiteList": false
            }
          ],
          "sites": [
            {
              "name": "Games",
              "sites": [{"name": "Steam", "host": "store.steampowered.com"}],
              "enabledByDefault": true,
              "isWhiteList": true
            },
            {
              "name": "VK",
              "sites": [],
              "enabledByDefault": false,
              "isWhiteList": false
            }
          ]
        }
        """;

    [Fact]
    public void Parse_windowsAndSites()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        Assert.Equal(2, catalog.AppGroups.Count);
        Assert.Equal("games", catalog.AppGroups[0].Id);
        Assert.Equal("steam.exe", catalog.AppGroups[0].Apps[0].Id);
        Assert.True(catalog.AppGroups[0].EnabledByDefault);
        Assert.True(catalog.AppGroups[0].IsWhiteList);
        Assert.False(catalog.AppGroups[1].EnabledByDefault);
        Assert.Single(catalog.SiteGroups[0].Sites);
        Assert.Equal("store.steampowered.com", catalog.SiteGroups[0].Sites[0].Host);
    }

    [Fact]
    public void IsGroupEnabled_usesEnabledByDefaultWithoutOverride()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var settings = TunSettings.Empty;
        Assert.True(settings.IsAppGroupEnabled(catalog.AppGroups[0], isFree: false));
        Assert.False(settings.IsAppGroupEnabled(catalog.AppGroups[1], isFree: false));
        Assert.False(settings.IsSiteGroupEnabled(catalog.SiteGroups[1], isFree: false));
    }

    [Fact]
    public void IsGroupEnabled_overrideWinsOverDefault()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var settings = new TunSettings([], [], new Dictionary<string, bool> { ["ru banks"] = true }, NoOverrides);
        Assert.True(settings.IsAppGroupEnabled(catalog.AppGroups[1], isFree: false));
    }

    [Fact]
    public void EffectiveBypassExes_freeFollowsApiDefaults_ignoresCustomAndOverrides()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var settings = new TunSettings(
            ["custom.exe"],
            [],
            new Dictionary<string, bool> { ["games"] = false },
            NoOverrides);
        var effective = settings.EffectiveBypassExes(catalog, isFree: true);
        Assert.DoesNotContain("custom.exe", effective);
        Assert.Contains("steam.exe", effective);
        Assert.DoesNotContain("sber.exe", effective);
    }

    [Fact]
    public void EffectiveBypassExes_paidRespectsOverrideAndDefault()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var settings = new TunSettings(
            ["custom.exe"],
            [],
            new Dictionary<string, bool> { ["games"] = false },
            NoOverrides);
        var effective = settings.EffectiveBypassExes(catalog, isFree: false);
        Assert.Equal(["custom.exe"], effective);
    }

    [Fact]
    public void EffectiveBypassHosts_paidDefaultOff()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var settings = TunSettings.Empty;
        var hosts = settings.EffectiveBypassHosts(catalog, isFree: false);
        Assert.Contains("store.steampowered.com", hosts);
        Assert.DoesNotContain(hosts, h => h.Contains("vk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EffectiveBypassHosts_paidUnionsCustom_freeIgnoresCustom()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var settings = new TunSettings([], ["youtube.com", "Example.COM"], NoOverrides, NoOverrides);
        var paid = settings.EffectiveBypassHosts(catalog, isFree: false);
        Assert.Contains("youtube.com", paid);
        Assert.Contains("example.com", paid);
        Assert.Contains("store.steampowered.com", paid);
        var free = settings.EffectiveBypassHosts(catalog, isFree: true);
        Assert.DoesNotContain("youtube.com", free);
        Assert.DoesNotContain("example.com", free);
        Assert.Contains("store.steampowered.com", free);
    }

    [Fact]
    public void RulesDisabled_clearsBypass_paid()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var settings = new TunSettings(["custom.exe"], ["youtube.com"], NoOverrides, NoOverrides, RulesEnabled: false);
        Assert.Empty(settings.EffectiveBypassExes(catalog, isFree: false));
        Assert.Empty(settings.EffectiveBypassHosts(catalog, isFree: false));
    }

    [Fact]
    public void Fingerprint_changesWithOverride()
    {
        var catalog = TunGroupsApi.Parse(SampleJson);
        var on = TunSettings.Empty.Fingerprint(catalog, isFree: false);
        var off = new TunSettings([], [], new Dictionary<string, bool> { ["games"] = false }, NoOverrides)
            .Fingerprint(catalog, isFree: false);
        Assert.NotEqual(on, off);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?", "youtube.com")]
    [InlineData("HTTP://Music.YouTube.com/path", "music.youtube.com")]
    [InlineData("example.com", "example.com")]
    [InlineData("www.Example.COM.", "example.com")]
    [InlineData("hello", null)]
    [InlineData("localhost", null)]
    [InlineData("1.2.3.4", null)]
    [InlineData("-bad.com", null)]
    public void TunBypassHosts_Parse_normalizesOrRejects(string raw, string? expected) =>
        Assert.Equal(expected, TunBypassHosts.Parse(raw));

    [Fact]
    public void TunBypassHosts_ParseMany_splitsCommaAndWhitespace()
    {
        var hosts = TunBypassHosts.ParseMany("https://www.youtube.com/a, example.com  hello  vk.com");
        Assert.Equal(["example.com", "vk.com", "youtube.com"], hosts);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "free", true)]
    [InlineData("free", "plus", true)]
    [InlineData("plus", "free", false)]
    [InlineData("vip", null, false)]
    [InlineData(null, "plus", false)]
    public void TunSubscription_IsFree_accountWinsOverSession(string? account, string? session, bool expected) =>
        Assert.Equal(expected, TunSubscription.IsFree(account, session));

    private static Dictionary<string, bool> NoOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
}
