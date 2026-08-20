using DemarVpn.Services;
using DemarVpn.Services.Support;

namespace DemarVpn.Tests;

public class DemarNavigationTest
{
    [Theory]
    [InlineData("demarvpn://auth/callback?token=abc", "abc", null)]
    [InlineData("demarvpn://auth/callback?code=xyz", null, "xyz")]
    [InlineData("demarvpn://auth/callback?code=xyz&token=abc", "abc", null)]
    [InlineData("demarvpn://auth?token=t", "t", null)]
    public void Auth_accepts_demarvpn_callback(string raw, string? token, string? code)
    {
        Assert.True(DemarNavigation.TryParseAuthHandoff(new Uri(raw), out var t, out var c));
        Assert.Equal(token, t);
        Assert.Equal(code, c);
    }

    [Theory]
    [InlineData("demar://auth/callback?token=abc")]
    [InlineData("https://auth/callback?token=abc")]
    [InlineData("demarvpn://oauth/callback?token=abc")]
    [InlineData("demarvpn://auth/other?token=abc")]
    [InlineData("demarvpn://auth/callback")]
    public void Auth_rejects_bad_uris(string raw)
    {
        Assert.False(DemarNavigation.TryParseAuthHandoff(new Uri(raw), out _, out _));
    }

    [Fact]
    public void Support_accepts_ticket_route()
    {
        Assert.True(DemarNavigation.TryParseSupportRoute(
            new Uri("demarvpn://settings/support/ticket/42"), out var route));
        Assert.Equal(new SupportRoute.Chat("42"), route);
    }

    [Fact]
    public void Support_accepts_open_prefix()
    {
        Assert.True(DemarNavigation.TryParseSupportRoute(
            new Uri("demarvpn://open/settings/support"), out var route));
        Assert.Null(route);
    }

    [Fact]
    public void Support_rejects_legacy_demar_scheme()
    {
        Assert.False(DemarNavigation.TryParseSupportRoute(
            new Uri("demar://settings/support"), out _));
    }
}
