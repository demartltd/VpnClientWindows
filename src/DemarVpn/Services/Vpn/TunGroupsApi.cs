using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services.Auth;

namespace DemarVpn.Services.Vpn;

public static class TunGroupsApi
{
    private const int MaxBytes = 2 * 1024 * 1024;

    public sealed record Payload(string Raw, TunCatalog Catalog);

    public static async Task<Payload?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await DemarHttp.GetStringAsync($"{DemarHttp.VpnApiBaseUrl}/tun", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(raw))
            {
                AppLog.Append("tun_groups: HTTP error");
                return null;
            }
            if (raw.Length > MaxBytes)
            {
                AppLog.Append("tun_groups: response too large");
                return null;
            }
            return new Payload(raw, Parse(raw));
        }
        catch (Exception ex)
        {
            AppLog.Append($"tun_groups: fetch failed — {ex.Message}");
            return null;
        }
    }

    public static TunCatalog Parse(string raw)
    {
        var root = DemarHttp.ParseObject(raw);
        return new TunCatalog(ParseAppGroups(root["windows"]), ParseSiteGroups(root["sites"]));
    }

    private static List<TunAppGroup> ParseAppGroups(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is not System.Text.Json.Nodes.JsonArray arr) return [];
        var list = new List<TunAppGroup>();
        foreach (var item in arr)
        {
            if (item is not System.Text.Json.Nodes.JsonObject obj) continue;
            var name = DemarHttp.OptString(obj, "name");
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new TunAppGroup(
                TunAppGroup.IdFor(name),
                name,
                ParseApps(obj["apps"]),
                DemarHttp.OptBool(obj, "enabledByDefault", true),
                DemarHttp.OptBool(obj, "isWhiteList", false)));
        }
        return list;
    }

    private static List<TunSiteGroup> ParseSiteGroups(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is not System.Text.Json.Nodes.JsonArray arr) return [];
        var list = new List<TunSiteGroup>();
        foreach (var item in arr)
        {
            if (item is not System.Text.Json.Nodes.JsonObject obj) continue;
            var name = DemarHttp.OptString(obj, "name");
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new TunSiteGroup(
                TunSiteGroup.IdFor(name),
                name,
                ParseSites(obj["sites"]),
                DemarHttp.OptBool(obj, "enabledByDefault", true),
                DemarHttp.OptBool(obj, "isWhiteList", false)));
        }
        return list;
    }

    private static List<TunAppRef> ParseApps(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is not System.Text.Json.Nodes.JsonArray arr) return [];
        var list = new List<TunAppRef>();
        foreach (var item in arr)
        {
            if (item is not System.Text.Json.Nodes.JsonObject obj) continue;
            var id = TunExeNames.Normalize(DemarHttp.OptString(obj, "id"));
            if (id.Length == 0) continue;
            var name = DemarHttp.OptString(obj, "name");
            list.Add(new TunAppRef(id, string.IsNullOrEmpty(name) ? id : name));
        }
        return list;
    }

    private static List<TunSiteRef> ParseSites(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is not System.Text.Json.Nodes.JsonArray arr) return [];
        var list = new List<TunSiteRef>();
        foreach (var item in arr)
        {
            if (item is not System.Text.Json.Nodes.JsonObject obj) continue;
            var host = DemarHttp.OptString(obj, "host");
            if (string.IsNullOrEmpty(host)) continue;
            var name = DemarHttp.OptString(obj, "name");
            list.Add(new TunSiteRef(host, string.IsNullOrEmpty(name) ? host : name));
        }
        return list;
    }
}
