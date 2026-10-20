using System.Text.Json;
using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Account;

namespace DemarVpn.Services.Servers;

/// <summary>Disk session cache — parity Apple SessionCacheStore (servers/account/traffic/tun).</summary>
public static class SessionCacheStore
{
    private static readonly object Gate = new();

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "session_cache.json");

    public sealed record Snapshot(
        IReadOnlyList<ServerProfile> Servers,
        bool TrafficExceeded,
        VpnAccount? Account,
        VpnTraffic? Traffic,
        string? TunRaw,
        IReadOnlyDictionary<string, ServerReachState> Reachability);

    public static Snapshot? Load(string userId)
    {
        try
        {
            var root = ReadRoot();
            if (root is null) return null;
            if (Opt(root, "userId") != userId) return null;
            var serversJson = Opt(root, "serversJson");
            if (string.IsNullOrWhiteSpace(serversJson)) return null;
            if (JsonNode.Parse(serversJson) is not JsonObject serversObj) return null;
            var parsed = ServersApi.ParseServersResponse(serversObj);
            var ids = parsed.Servers.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var reach = ServerReachState.DecodeCache(Opt(root, "reachabilityJson"))
                .Where(kv => ids.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            return new Snapshot(
                parsed.Servers,
                parsed.TrafficExceeded,
                ParseAccount(Opt(root, "accountJson")),
                ParseTraffic(Opt(root, "trafficJson")),
                Opt(root, "tunRaw"),
                reach);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveServers(string rawJson, string userId)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return;
        Mutate(userId, root => root["serversJson"] = rawJson);
    }

    public static void SaveAccount(VpnAccount account, string userId) =>
        Mutate(userId, root => root["accountJson"] = AccountJson(account));

    public static void SaveTraffic(VpnTraffic traffic, string userId) =>
        Mutate(userId, root => root["trafficJson"] = TrafficJson(traffic));

    public static void SaveTun(string raw, string userId)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return;
        Mutate(userId, root => root["tunRaw"] = trimmed);
    }

    public static void SaveReachability(IReadOnlyDictionary<string, ServerReachState> map, string userId) =>
        Mutate(userId, root => root["reachabilityJson"] = ServerReachState.EncodeCache(map));

    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
        }
        catch { /* ignore */ }
    }

    private static void Mutate(string userId, Action<JsonObject> edit)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        try
        {
            lock (Gate)
            {
                var root = ReadRootUnlocked() ?? new JsonObject();
                root["userId"] = userId;
                edit(root);
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, root.ToJsonString());
            }
        }
        catch { /* ignore */ }
    }

    private static JsonObject? ReadRoot()
    {
        lock (Gate) return ReadRootUnlocked();
    }

    private static JsonObject? ReadRootUnlocked()
    {
        if (!File.Exists(FilePath)) return null;
        return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
    }

    private static string? Opt(JsonObject root, string key) =>
        root[key]?.GetValueKind() == JsonValueKind.String ? root[key]!.GetValue<string>() : null;

    private static VpnAccount? ParseAccount(string? json) =>
        TryParse(json, VpnAccountApi.ParseAccount);

    private static VpnTraffic? ParseTraffic(string? json) =>
        TryParse(json, VpnAccountApi.ParseTraffic);

    private static T? TryParse<T>(string? json, Func<JsonObject, T> parse) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) is JsonObject obj ? parse(obj) : null; }
        catch { return null; }
    }

    private static string AccountJson(VpnAccount a) => new JsonObject
    {
        ["notifyNews"] = a.NotifyNews,
        ["notifyImportant"] = a.NotifyImportant,
        ["vpnSubscription"] = a.VpnSubscription,
        ["vpnExpiresAt"] = a.VpnExpiresAt,
    }.ToJsonString();

    private static string TrafficJson(VpnTraffic t)
    {
        var servers = new JsonArray();
        foreach (var s in t.Servers)
            servers.Add(new JsonObject { ["name"] = s.Name, ["bytes"] = s.Bytes });
        return new JsonObject
        {
            ["y"] = t.Y,
            ["m"] = t.M,
            ["total"] = t.Total,
            ["limitBytes"] = t.LimitBytes,
            ["servers"] = servers,
        }.ToJsonString();
    }
}
