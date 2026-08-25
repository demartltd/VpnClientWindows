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

    [Theory]
    [InlineData("demarvpn://settings/traffic")]
    [InlineData("demarvpn://open/settings/traffic")]
    public void Traffic_accepts_settings_traffic(string raw)
    {
        Assert.True(DemarNavigation.TryParseTrafficRoute(new Uri(raw)));
    }

    [Theory]
    [InlineData("demarvpn://pay/ok", true)]
    [InlineData("demarvpn://pay/fail", false)]
    [InlineData("demarvpn://open/pay/ok", true)]
    public void Pay_accepts_ok_fail(string raw, bool success)
    {
        Assert.True(DemarNavigation.TryParsePayReturn(new Uri(raw), out var ok));
        Assert.Equal(success, ok);
    }

    [Theory]
    [InlineData("demarvpn://pay/other")]
    [InlineData("demarvpn://settings/pay/ok")]
    [InlineData("https://pay/ok")]
    public void Pay_rejects_bad_uris(string raw) =>
        Assert.False(DemarNavigation.TryParsePayReturn(new Uri(raw), out _));

    [Theory]
    [InlineData("demarvpn://account/buy", DemarNavigation.AccountSheet.Buy)]
    [InlineData("demarvpn://account/plans", DemarNavigation.AccountSheet.Plans)]
    [InlineData("demarvpn://account/traffic", DemarNavigation.AccountSheet.Traffic)]
    [InlineData("demarvpn://open/account/buy", DemarNavigation.AccountSheet.Buy)]
    public void Account_accepts_sheet_routes(string raw, DemarNavigation.AccountSheet sheet)
    {
        Assert.True(DemarNavigation.TryParseAccountSheet(new Uri(raw), out var parsed));
        Assert.Equal(sheet, parsed);
    }

    [Fact]
    public void Account_rejects_unknown() =>
        Assert.False(DemarNavigation.TryParseAccountSheet(new Uri("demarvpn://account/family"), out _));
}
