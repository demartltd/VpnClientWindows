using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Servers;

public sealed record ServersListResult(
    string? VpnSubscription,
    string? VpnExpiresAt,
    IReadOnlyList<ServerProfile> Servers);

public static class ServersApi
{
    public static async Task<ServersListResult> FetchServersAsync(
        string userId, string loginAt, CancellationToken ct = default)
    {
        var json = await DemarHttp.PostJsonAsync(
            $"{DemarHttp.BaseUrl}/auth/mobile/servers",
            new JsonObject { ["userId"] = userId, ["loginAt"] = loginAt },
            ct);
        var profiles = new List<ServerProfile>();
        if (json["servers"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject item) continue;
                var profile = ParseServerItem(item);
                if (profile is not null) profiles.Add(profile);
                else
                {
                    var proto = DemarHttp.OptString(item, "protocol");
                    var name = DemarHttp.OptString(item, "name");
                    AppLog.Append($"servers: skip unsupported protocol={proto} name={name}");
                }
            }
        }
        return new ServersListResult(
            DemarHttp.OptString(json, "vpnSubscription"),
            DemarHttp.OptString(json, "vpnExpiresAt"),
            profiles);
    }

    public static ServerProfile? ParseServerItem(JsonObject item)
    {
        var id = DemarHttp.OptString(item, "id");
        var name = DemarHttp.OptString(item, "name");
        if (id is null || name is null) return null;
        var city = DemarHttp.OptString(item, "city");
        var protocol = DemarHttp.OptString(item, "protocol")?.ToLowerInvariant();
        var configRaw = DemarHttp.OptString(item, "clientConfigJson");
        if (protocol != "hysteria2" || configRaw is null) return null;
        JsonObject? config;
        try { config = JsonNode.Parse(configRaw) as JsonObject; }
        catch (JsonException) { return null; }
        return config is null ? null : ParseHysteria2(id, name, city, config);
    }

    private static ServerProfile? ParseHysteria2(string id, string name, string? city, JsonObject config)
    {
        if (config["outbounds"] is not JsonArray outbounds) return null;
        JsonObject? hy2 = null;
        foreach (var n in outbounds)
        {
            if (n is JsonObject o && DemarHttp.OptString(o, "type") == "hysteria2")
            {
                hy2 = o;
                break;
            }
        }
        if (hy2 is null) return null;
        var server = DemarHttp.OptString(hy2, "server");
        var port = DemarHttp.OptInt(hy2, "server_port");
        var password = DemarHttp.OptString(hy2, "password");
        if (server is null || port <= 0 || password is null) return null;

        var tls = hy2["tls"] as JsonObject;
        var tlsName = (tls is not null ? DemarHttp.OptString(tls, "server_name") : null) ?? server;
        var alpn = new List<string>();
        if (tls?["alpn"] is JsonArray alpnArr)
        {
            foreach (var a in alpnArr)
            {
                var s = a?.GetValueKind() == JsonValueKind.String ? a.GetValue<string>() : null;
                if (!string.IsNullOrWhiteSpace(s)) alpn.Add(s);
            }
        }
        string? obfs = null;
        if (hy2["obfs"] is JsonObject obfsObj)
            obfs = DemarHttp.OptString(obfsObj, "password");
        var outboundCity = DemarHttp.OptString(hy2, "city") ?? city;

        return ServerProfile.Hysteria2FromTag(
            new Hysteria2Profile(name, server, port, password, tlsName, alpn.Count > 0 ? alpn : null, obfs),
            city: outboundCity,
            id: id);
    }
}
