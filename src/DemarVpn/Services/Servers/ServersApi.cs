using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Servers;

public sealed record ServersListResult(
    string? VpnSubscription,
    string? VpnExpiresAt,
    IReadOnlyList<ServerProfile> Servers,
    bool TrafficExceeded = false,
    string RawJson = "");

/// <summary>VPN servers — parity Android ServersApi (POST vpn.example.invalid/vpn/servers).</summary>
public static class ServersApi
{
    public static async Task<ServersListResult> FetchServersAsync(
        string userId, string loginAt, CancellationToken ct = default)
    {
        var json = await DemarHttp.PostJsonAsync(
            $"{DemarHttp.VpnApiBaseUrl}/vpn/servers",
            new JsonObject { ["userId"] = userId, ["loginAt"] = loginAt },
            ct).ConfigureAwait(false);
        return ParseServersResponse(json);
    }

    public static ServersListResult ParseServersResponse(JsonObject json)
    {
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
            profiles,
            json["trafficExceeded"]?.GetValueKind() == JsonValueKind.True,
            json.ToJsonString());
    }

    public static ServerProfile? ParseServerItem(JsonObject item)
    {
        var id = DemarHttp.OptString(item, "id");
        var name = DemarHttp.OptString(item, "name");
        if (id is null || name is null) return null;

        var protocol = ServerProtocolLabels.FromApi(DemarHttp.OptString(item, "protocol"));
        if (protocol is null) return null;

        var city = DemarHttp.OptString(item, "city");
        var configRaw = DemarHttp.OptString(item, "clientConfigJson");
        // No credentials (traffic exceeded / preview): show in UI, block connect separately.
        if (string.IsNullOrWhiteSpace(configRaw))
            return Preview(protocol.Value, id, name, city);

        JsonObject? config;
        try { config = JsonNode.Parse(configRaw) as JsonObject; }
        catch (JsonException) { return null; }
        if (config is null) return null;

        return protocol.Value switch
        {
            ServerProtocol.Hysteria2 => ParseHysteria2(id, name, city, config),
            ServerProtocol.Trojan => ParseTrojan(id, name, city, config),
            ServerProtocol.VlessReality => ParseVlessReality(id, name, city, config),
            _ => null,
        };
    }

    private static ServerProfile Preview(ServerProtocol protocol, string id, string name, string? city)
    {
        IProxyProfile empty = protocol switch
        {
            ServerProtocol.Trojan =>
                new TrojanProfile(name, "", 443, "", name),
            ServerProtocol.VlessReality =>
                new VlessRealityProfile(name, "", 443, "", name, "", ""),
            _ =>
                new Hysteria2Profile(name, "", 443, "", name),
        };
        return ServerProfile.FromTag(empty, city: city, id: id);
    }

    private static ServerProfile? ParseHysteria2(string id, string name, string? city, JsonObject config)
    {
        var hy2 = FirstOutbound(config, "hysteria2");
        if (hy2 is null || DialFields(hy2) is not { } dial) return null;
        var (server, port) = dial;

        var password = DemarHttp.OptString(hy2, "password") ?? "";
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

        return ServerProfile.FromTag(
            new Hysteria2Profile(
                name, server, port, password, tlsName,
                alpn is { Count: > 0 } ? alpn : null,
                obfs,
                PositiveMbps(hy2, "up_mbps"),
                PositiveMbps(hy2, "down_mbps")),
            city: DemarHttp.OptString(hy2, "city") ?? city,
            id: id,
            bypassRuleSets: ParseBypassRuleSets(config));
    }

    private static ServerProfile? ParseTrojan(string id, string name, string? city, JsonObject config)
    {
        var trojan = FirstOutbound(config, "trojan");
        if (trojan is null || DialFields(trojan) is not { } dial) return null;
        var (server, port) = dial;

        var password = DemarHttp.OptString(trojan, "password") ?? "";
        var tls = trojan["tls"] as JsonObject;
        var tlsName = (tls is not null ? DemarHttp.OptString(tls, "server_name") : null) ?? server;

        return ServerProfile.FromTag(
            new TrojanProfile(name, server, port, password, tlsName),
            city: DemarHttp.OptString(trojan, "city") ?? city,
            id: id,
            bypassRuleSets: ParseBypassRuleSets(config));
    }

    private static ServerProfile? ParseVlessReality(string id, string name, string? city, JsonObject config)
    {
        var vless = FirstOutbound(config, "vless");
        if (vless is null || DialFields(vless) is not { } dial) return null;
        var (server, port) = dial;

        var uuid = DemarHttp.OptString(vless, "uuid") ?? "";
        var tls = vless["tls"] as JsonObject;
        var reality = tls?["reality"] as JsonObject;
        var publicKey = reality is not null ? DemarHttp.OptString(reality, "public_key") : null;
        var shortId = reality is not null ? DemarHttp.OptString(reality, "short_id") : null;
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(shortId)) return null;

        var tlsName = (tls is not null ? DemarHttp.OptString(tls, "server_name") : null)
            ?? "www.cloudflare.com";
        var flow = DemarHttp.OptString(vless, "flow");

        return ServerProfile.FromTag(
            new VlessRealityProfile(
                name, server, port, uuid, tlsName, publicKey, shortId,
                string.IsNullOrWhiteSpace(flow) ? "xtls-rprx-vision" : flow),
            city: DemarHttp.OptString(vless, "city") ?? city,
            id: id,
            bypassRuleSets: ParseBypassRuleSets(config));
    }

    private static JsonObject? FirstOutbound(JsonObject config, string type)
    {
        if (config["outbounds"] is not JsonArray outbounds) return null;
        return outbounds.OfType<JsonObject>()
            .FirstOrDefault(o => DemarHttp.OptString(o, "type") == type);
    }

    private static (string Server, int Port)? DialFields(JsonObject outbound)
    {
        var server = DemarHttp.OptString(outbound, "server");
        var port = DemarHttp.OptInt(outbound, "server_port");
        if (server is null || port <= 0) return null;
        return (server, port);
    }

    private static int? PositiveMbps(JsonObject obj, string key)
    {
        var value = DemarHttp.OptInt(obj, key);
        return value > 0 ? value : null;
    }

    /// <summary>
    /// Rule-set tags from API (<c>type: remote</c> binary). Tag only; URL ignored at runtime (bundled local).
    /// Ignores TUN <c>route_exclude_address_set</c> (parity Android).
    /// </summary>
    public static IReadOnlyList<ApiBypassRuleSet> ParseBypassRuleSets(JsonObject config)
    {
        if (config["route"] is not JsonObject route || route["rule_set"] is not JsonArray arr)
            return [];

        var byTag = new Dictionary<string, ApiBypassRuleSet>(StringComparer.Ordinal);
        foreach (var node in arr)
        {
            if (node is not JsonObject item) continue;
            var tag = DemarHttp.OptString(item, "tag")?.Trim();
            var url = DemarHttp.OptString(item, "url")?.Trim();
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url)) continue;
            var type = DemarHttp.OptString(item, "type") ?? "remote";
            var format = DemarHttp.OptString(item, "format") ?? "binary";
            if (type != "remote" || format != "binary") continue;
            byTag.TryAdd(tag, new ApiBypassRuleSet(tag));
        }
        return byTag.Values.ToList();
    }
}
