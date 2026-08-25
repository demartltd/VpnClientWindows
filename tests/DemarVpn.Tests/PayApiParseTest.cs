using System.Text.Json.Nodes;
using DemarVpn.Services.Account;
using Xunit;

namespace DemarVpn.Tests;

public class PayApiParseTest
{
    [Fact]
    public void ParseCatalog_readsTiersAndPrices()
    {
        var json = JsonNode.Parse(
            """
            {
              "plans": [
                {
                  "tier": "plus",
                  "periods": [
                    {"period": "month", "days": 30, "prices": {"RUB": "199.00", "EUR": "4.00"}},
                    {"period": "year", "days": 365, "prices": {"RUB": "1599.00", "EUR": "32.00"}}
                  ]
                },
                {
                  "tier": "vip",
                  "periods": [
                    {"period": "month", "days": 30, "prices": {"RUB": "399.00", "EUR": "8.00"}}
                  ]
                }
              ]
            }
            """) as JsonObject;
        var catalog = PayApi.ParseCatalog(json!);
        Assert.Equal(2, catalog.Tiers.Count);
        Assert.Equal("199.00", catalog.Price("plus", "month", "RUB"));
        Assert.Equal("8.00", catalog.Price("vip", "month", "EUR"));
        Assert.Null(catalog.Price("free", "month", "RUB"));
    }

    [Fact]
    public void ParseCheckout_readsFields()
    {
        var json = JsonNode.Parse(
            """
            {
              "orderId": "ord_1",
              "payUrl": "https://rollypay.io/pay/abc",
              "amount": "199.00",
              "currency": "RUB",
              "discountPercent": 10
            }
            """) as JsonObject;
        var checkout = PayApi.ParseCheckout(json!)!;
        Assert.Equal("ord_1", checkout.OrderId);
        Assert.Equal("https://rollypay.io/pay/abc", checkout.PayUrl);
        Assert.Equal("199.00", checkout.Amount);
        Assert.Equal("RUB", checkout.Currency);
        Assert.Equal(10, checkout.DiscountPercent);
    }

    [Fact]
    public void ParseCheckout_rejectsMissingPayUrl()
    {
        var json = JsonNode.Parse("""{"orderId":"ord_1","amount":"1.00"}""") as JsonObject;
        Assert.Null(PayApi.ParseCheckout(json!));
    }

    [Fact]
    public void FormatPrice_rubAndEur()
    {
        Assert.Equal("199 ₽", PayApi.FormatPrice("199.00", "RUB"));
        Assert.Equal("€4", PayApi.FormatPrice("4.00", "EUR"));
        Assert.Equal("€1.5", PayApi.FormatPrice("1.50", "EUR"));
    }

    [Fact]
    public void DiscountedDisplay_appliesFloor()
    {
        Assert.Equal("179 ₽", PayApi.DiscountedDisplay("199.00", 10, "RUB"));
        Assert.Equal("€3.6", PayApi.DiscountedDisplay("4.00", 10, "EUR"));
    }

    [Theory]
    [InlineData(CheckoutIntent.Buy, "free", "plus")]
    [InlineData(CheckoutIntent.Renew, "vip", "vip")]
    [InlineData(CheckoutIntent.UpgradeTraffic, "plus", "vip")]
    [InlineData(CheckoutIntent.UpgradeTraffic, "free", "plus")]
    public void DefaultTier(CheckoutIntent intent, string current, string expected) =>
        Assert.Equal(expected, intent.DefaultTier(current));
}
