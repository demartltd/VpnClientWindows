using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Account;

public sealed record PayPeriodPrices(string Rub, string Eur)
{
    public string Amount(string currency) =>
        currency.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? Eur : Rub;
}

public sealed record PayPeriodOption(string Period, int Days, PayPeriodPrices Prices);

public sealed record PayTierOption(string Tier, IReadOnlyList<PayPeriodOption> Periods);

public sealed record PayCatalog(IReadOnlyList<PayTierOption> Tiers)
{
    public string? Price(string tier, string period, string currency) =>
        Tiers.FirstOrDefault(t => t.Tier.Equals(tier, StringComparison.OrdinalIgnoreCase))
            ?.Periods.FirstOrDefault(p => p.Period.Equals(period, StringComparison.OrdinalIgnoreCase))
            ?.Prices.Amount(currency);
}

public sealed record PayCheckout(
    string OrderId,
    string PayUrl,
    string Amount,
    string Currency,
    int? DiscountPercent);

/// <summary>Pay catalog / checkout — parity Android PayApi (vpn.example.invalid).</summary>
public static class PayApi
{
    public static async Task<PayCatalog> FetchPlansAsync(CancellationToken ct = default) =>
        ParseCatalog(await DemarHttp.GetJsonAsync($"{DemarHttp.VpnApiBaseUrl}/vpn/pay/plans", ct: ct));

    public static async Task<PayCheckout> CreateAsync(
        string userId, string loginAt, string tier, string period, string currency, string? code = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["userId"] = userId,
            ["loginAt"] = loginAt,
            ["tier"] = tier,
            ["period"] = period,
            ["currency"] = currency.ToUpperInvariant(),
        };
        if (!string.IsNullOrWhiteSpace(code)) body["code"] = code;
        return ParseCheckout(await DemarHttp.PostJsonAsync($"{DemarHttp.VpnApiBaseUrl}/vpn/pay/create", body, ct))
               ?? throw new AuthException("invalid_checkout", 502);
    }

    public static PayCatalog ParseCatalog(JsonObject json)
    {
        var tiers = new List<PayTierOption>();
        if (json["plans"] is not JsonArray rows) return new(tiers);
        foreach (var node in rows)
        {
            if (node is not JsonObject row) continue;
            var tier = DemarHttp.OptString(row, "tier");
            if (tier is null) continue;
            var periods = new List<PayPeriodOption>();
            if (row["periods"] is JsonArray periodsArr)
            {
                foreach (var pNode in periodsArr)
                {
                    if (pNode is not JsonObject p) continue;
                    var period = DemarHttp.OptString(p, "period");
                    if (period is null) continue;
                    var prices = p["prices"] as JsonObject ?? new JsonObject();
                    var days = DemarHttp.OptInt(p, "days");
                    periods.Add(new(
                        period,
                        days < 0 ? 0 : days,
                        new(StringPrice(prices["RUB"]), StringPrice(prices["EUR"]))));
                }
            }
            tiers.Add(new(tier, periods));
        }
        return new(tiers);
    }

    public static PayCheckout? ParseCheckout(JsonObject json)
    {
        var orderId = DemarHttp.OptString(json, "orderId");
        var payUrl = DemarHttp.OptString(json, "payUrl");
        if (orderId is null || payUrl is null) return null;
        int? discount = null;
        if (json.ContainsKey("discountPercent"))
        {
            var d = DemarHttp.OptInt(json, "discountPercent");
            if (d >= 0) discount = d;
        }
        return new(
            orderId,
            payUrl,
            StringPrice(json["amount"]),
            (DemarHttp.OptString(json, "currency") ?? "RUB").ToUpperInvariant(),
            discount);
    }

    /** RUB whole rubles; EUR tenths (site parity). */
    public static string FormatPrice(string amount, string currency)
    {
        var n = double.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        if (currency.Equals("EUR", StringComparison.OrdinalIgnoreCase))
        {
            var rounded = Math.Round(n * 10) / 10;
            return Math.Abs(rounded - Math.Round(rounded)) < 0.0001
                ? $"€{(int)Math.Round(rounded)}"
                : string.Format(CultureInfo.InvariantCulture, "€{0:0.0}", rounded);
        }
        return $"{(int)Math.Round(n)} ₽";
    }

    public static string DiscountedDisplay(string major, int percent, string currency)
    {
        var n = double.TryParse(major, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var p = Math.Clamp(percent, 0, 100);
        var raw = n * (100 - p) / 100.0;
        string amount;
        if (currency.Equals("EUR", StringComparison.OrdinalIgnoreCase))
            amount = string.Format(CultureInfo.InvariantCulture, "{0:0.00}", Math.Max(1.0, Math.Round(raw * 10) / 10));
        else
            amount = Math.Max(30, (int)Math.Round(raw)).ToString(CultureInfo.InvariantCulture);
        return FormatPrice(amount, currency);
    }

    private static string StringPrice(JsonNode? value)
    {
        if (value is null || value.GetValueKind() is JsonValueKind.Null or JsonValueKind.Undefined)
            return "0.00";
        try
        {
            if (value.GetValueKind() == JsonValueKind.Number)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00}", value.GetValue<double>());
            var s = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString().Trim('"');
            return string.IsNullOrWhiteSpace(s) ? "0.00" : s;
        }
        catch { return "0.00"; }
    }
}
