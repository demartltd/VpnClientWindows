using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Account;

/// <summary>Account / traffic / notify — parity Android VpnAccountApi (vpn.example.invalid).</summary>
public static class VpnAccountApi
{
    public static async Task<VpnAccount> FetchAccountAsync(string userId, string loginAt, CancellationToken ct = default) =>
        ParseAccount(await PostAsync("/vpn/account", SessionBody(userId, loginAt), ct));

    public static async Task<VpnTraffic> FetchTrafficAsync(string userId, string loginAt, CancellationToken ct = default) =>
        ParseTraffic(await PostAsync("/vpn/traffic", SessionBody(userId, loginAt), ct));

    public static async Task<VpnNotify> UpdateNotifyAsync(
        string userId, string loginAt, bool? notifyNews = null, bool? notifyImportant = null, CancellationToken ct = default)
    {
        var body = SessionBody(userId, loginAt);
        if (notifyNews is not null) body["notifyNews"] = notifyNews;
        if (notifyImportant is not null) body["notifyImportant"] = notifyImportant;
        var json = await PostAsync("/vpn/notify", body, ct);
        return new(
            DemarHttp.OptBool(json, "notifyNews", true),
            DemarHttp.OptBool(json, "notifyImportant", true));
    }

    public static VpnAccount ParseAccount(JsonObject json) => new(
        DemarHttp.OptBool(json, "notifyNews", true),
        DemarHttp.OptBool(json, "notifyImportant", true),
        DemarHttp.OptString(json, "vpnSubscription"),
        DemarHttp.OptString(json, "vpnExpiresAt"));

    public static VpnTraffic ParseTraffic(JsonObject json)
    {
        var servers = new List<VpnTrafficServer>();
        if (json["servers"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject item) continue;
                var name = DemarHttp.OptString(item, "name");
                if (name is null) continue;
                servers.Add(new(name, Math.Max(0, OptLong(item, "bytes"))));
            }
        }
        return new(
            Math.Max(0, DemarHttp.OptInt(json, "y")),
            Math.Max(0, DemarHttp.OptInt(json, "m")),
            servers,
            Math.Max(0, OptLong(json, "total")),
            Math.Max(0, OptLong(json, "limitBytes")));
    }

    /** Collapse to ≤6 slices: all if ≤6, else top-5 + Other. */
    public static IReadOnlyList<VpnTrafficServer> PieSlices(IReadOnlyList<VpnTrafficServer> servers, string otherLabel)
    {
        if (servers.Count <= 6) return servers;
        var top = servers.Take(5).ToList();
        var rest = servers.Skip(5).Sum(s => s.Bytes);
        if (rest > 0) top.Add(new(otherLabel, rest));
        return top;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var exp = (int)Math.Clamp(Math.Log(bytes) / Math.Log(1024), 1, 4);
        var value = bytes / Math.Pow(1024, exp);
        var unit = exp switch { 1 => "KB", 2 => "MB", 3 => "GB", _ => "TB" };
        return string.Format(CultureInfo.InvariantCulture, value >= 100 ? "{0:0} {1}" : "{0:0.0} {1}", value, unit);
    }

    private static JsonObject SessionBody(string userId, string loginAt) =>
        new() { ["userId"] = userId, ["loginAt"] = loginAt };

    private static Task<JsonObject> PostAsync(string path, JsonObject body, CancellationToken ct) =>
        DemarHttp.PostJsonAsync($"{DemarHttp.VpnApiBaseUrl}{path}", body, ct);

    private static long OptLong(JsonObject json, string key)
    {
        if (!json.TryGetPropertyValue(key, out var n) || n is null) return 0;
        try
        {
            return n.GetValueKind() switch
            {
                JsonValueKind.Number => n.GetValue<long>(),
                JsonValueKind.String when long.TryParse(n.GetValue<string>(), out var v) => v,
                _ => 0,
            };
        }
        catch { return 0; }
    }
}
