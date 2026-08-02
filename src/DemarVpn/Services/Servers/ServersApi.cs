using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Servers;

public sealed record ServersListResult(
    string? VpnSubscription,
    string? VpnExpiresAt,
    IReadOnlyList<ServerProfile> Servers);

/// <summary>VPN servers — parity Android ServersApi (POST vpn.example.invalid/vpn/servers).</summary>
public static class ServersApi
{
    public static async Task<ServersListResult> FetchServersAsync(
        string userId, string loginAt, CancellationToken ct = default)
    {
        var json = await DemarHttp.PostJsonAsync(
            $"{DemarHttp.VpnApiBaseUrl}/vpn/servers",
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
                    AppLog.Append(
                        $"servers: skip unsupported protocol={DemarHttp.OptString(item, "protocol")} name={DemarHttp.OptString(item, "name")}");
            }
        }

        return new(
            DemarHttp.OptString(json, "vpnSubscription"),
            DemarHttp.OptString(json, "vpnExpiresAt"),
            profiles);
    }

    public static ServerProfile? ParseServerItem(JsonObject item)
    {
        var id = DemarHttp.OptString(item, "id");
        var name = DemarHttp.OptString(item, "name");
        if (id is null || name is null) return null;

        var protocol = DemarHttp.OptString(item, "protocol")?.ToLowerInvariant();
        var configRaw = DemarHttp.OptString(item, "clientConfigJson");
        if (configRaw is null) return null;

        JsonObject? config;
        try { config = JsonNode.Parse(configRaw) as JsonObject; }
        catch (JsonException) { return null; }
        if (config is null) return null;

        return protocol switch
        {
            "hysteria2" => ParseHysteria2(id, name, DemarHttp.OptString(item, "city"), config),
            _ => null,
        };
    }

    private static ServerProfile? ParseHysteria2(string id, string name, string? city, JsonObject config)
    {
        if (config["outbounds"] is not JsonArray outbounds) return null;
        var hy2 = outbounds.OfType<JsonObject>()
            .FirstOrDefault(o => DemarHttp.OptString(o, "type") == "hysteria2");
        if (hy2 is null) return null;

        var server = DemarHttp.OptString(hy2, "server");
        var port = DemarHttp.OptInt(hy2, "server_port");
        var password = DemarHttp.OptString(hy2, "password");
        if (server is null || port <= 0 || password is null) return null;

        var tls = hy2["tls"] as JsonObject;
        var tlsName = (tls is not null ? DemarHttp.OptString(tls, "server_name") : null) ?? server;
        var alpn = tls?["alpn"] is JsonArray alpnArr
            ? alpnArr
                .Where(a => a?.GetValueKind() == JsonValueKind.String)
                .Select(a => a!.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
            : null;
        var obfs = hy2["obfs"] is JsonObject obfsObj ? DemarHttp.OptString(obfsObj, "password") : null;

        return ServerProfile.Hysteria2FromTag(
            new Hysteria2Profile(name, server, port, password, tlsName, alpn is { Count: > 0 } ? alpn : null, obfs),
            city: DemarHttp.OptString(hy2, "city") ?? city,
            id: id);
    }
}
