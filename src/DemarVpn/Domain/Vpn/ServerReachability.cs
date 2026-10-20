using System.Text.Json;
using System.Text.Json.Nodes;

namespace DemarVpn.Domain.Vpn;

public enum ServerReachStatus
{
    Checking,
    Online,
    Offline,
}

public sealed record ServerReachState(
    ServerReachStatus Status,
    int? LatencyMs = null,
    int? TlsMs = null)
{
    public static ServerReachState Checking { get; } = new(ServerReachStatus.Checking);
    public static ServerReachState Offline { get; } = new(ServerReachStatus.Offline);

    public static string FormatMs(int? ms) => ms is int v ? $"~{v}ms" : "n/a";

    public static ServerReachState FromProbes(int tcpMs, int pingMs)
    {
        var tcpOk = tcpMs >= 0;
        var pingOk = pingMs >= 0;
        if (!tcpOk && !pingOk) return Offline;
        return new ServerReachState(
            ServerReachStatus.Online,
            tcpOk ? tcpMs : null,
            pingOk ? pingMs : null);
    }

    /// <summary>Stable cache entry — skip in-flight Checking without metrics (parity Apple/Android).</summary>
    public JsonObject? ToCacheJson()
    {
        if (Status == ServerReachStatus.Checking && LatencyMs is null && TlsMs is null) return null;
        var obj = new JsonObject
        {
            ["status"] = Status == ServerReachStatus.Offline
                ? nameof(ServerReachStatus.Offline)
                : nameof(ServerReachStatus.Online),
        };
        if (LatencyMs is int tcp) obj["latencyMs"] = tcp;
        if (TlsMs is int tls) obj["tlsMs"] = tls;
        return obj;
    }

    public static ServerReachState? FromCacheJson(JsonObject obj)
    {
        var status = Opt(obj, "status") switch
        {
            nameof(ServerReachStatus.Offline) => ServerReachStatus.Offline,
            nameof(ServerReachStatus.Online) => ServerReachStatus.Online,
            _ => (ServerReachStatus?)null,
        };
        if (status is null) return null;
        return new ServerReachState(status.Value, OptMs(obj, "latencyMs"), OptMs(obj, "tlsMs"));
    }

    public static string EncodeCache(IReadOnlyDictionary<string, ServerReachState> map)
    {
        var root = new JsonObject();
        foreach (var (id, state) in map)
            if (state.ToCacheJson() is { } json) root[id] = json;
        return root.ToJsonString();
    }

    public static Dictionary<string, ServerReachState> DecodeCache(string? json)
    {
        var outMap = new Dictionary<string, ServerReachState>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return outMap;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return outMap;
            foreach (var (key, node) in root)
            {
                if (node is not JsonObject obj) continue;
                if (FromCacheJson(obj) is { } state) outMap[key] = state;
            }
        }
        catch { /* ignore */ }
        return outMap;
    }

    private static string? Opt(JsonObject obj, string key) =>
        obj[key]?.GetValueKind() == JsonValueKind.String ? obj[key]!.GetValue<string>() : null;

    private static int? OptMs(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue v) return null;
        try
        {
            var n = v.GetValue<int>();
            return n >= 0 ? n : null;
        }
        catch { return null; }
    }
}
