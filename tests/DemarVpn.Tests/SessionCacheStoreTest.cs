using System.Text.Json.Nodes;
using DemarVpn.Domain.Vpn;
using DemarVpn.Services.Account;
using DemarVpn.Services.Servers;

namespace DemarVpn.Tests;

public class SessionCacheStoreTest : IDisposable
{
    private readonly string _prevPath;
    private readonly string _tempPath;

    public SessionCacheStoreTest()
    {
        _prevPath = SessionCacheStore.FilePath;
        _tempPath = Path.Combine(Path.GetTempPath(), "DemarVpnTests", Guid.NewGuid().ToString("N"), "session_cache.json");
        SessionCacheStore.FilePath = _tempPath;
    }

    public void Dispose()
    {
        SessionCacheStore.Clear();
        SessionCacheStore.FilePath = _prevPath;
        try
        {
            var dir = Path.GetDirectoryName(_tempPath);
            if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { /* ignore */ }
    }

    private const string ServersSample =
        """
        {
          "vpnSubscription": "plus",
          "trafficExceeded": true,
          "servers": [
            { "id": "s1", "name": "Germany", "city": "Frankfurt", "protocol": "hysteria2" }
          ]
        }
        """;

    [Fact]
    public void SaveLoad_fullSessionRoundTrip()
    {
        SessionCacheStore.SaveServers(ServersSample, "user-a");
        SessionCacheStore.SaveAccount(new VpnAccount(true, false, "plus", "2099-01-01"), "user-a");
        SessionCacheStore.SaveTraffic(new VpnTraffic(2026, 9, [new("DE", 100)], 100, 1000), "user-a");
        SessionCacheStore.SaveTun("""{"windows":[],"sites":[]}""", "user-a");

        var snap = SessionCacheStore.Load("user-a");
        Assert.NotNull(snap);
        Assert.Single(snap.Servers);
        Assert.Equal("s1", snap.Servers[0].Id);
        Assert.True(snap.TrafficExceeded);
        Assert.NotNull(snap.Account);
        Assert.Equal("plus", snap.Account.VpnSubscription);
        Assert.False(snap.Account.NotifyImportant);
        Assert.NotNull(snap.Traffic);
        Assert.Equal(100, snap.Traffic.Total);
        Assert.Equal("""{"windows":[],"sites":[]}""", snap.TunRaw);
    }

    [Fact]
    public void Load_wrongUser_returnsNull()
    {
        SessionCacheStore.SaveServers(ServersSample, "user-a");
        Assert.Null(SessionCacheStore.Load("user-b"));
    }

    [Fact]
    public void ParseServersResponse_readsTrafficExceeded()
    {
        var json = JsonNode.Parse(ServersSample) as JsonObject;
        var result = ServersApi.ParseServersResponse(json!);
        Assert.True(result.TrafficExceeded);
        Assert.Single(result.Servers);
        Assert.False(string.IsNullOrWhiteSpace(result.RawJson));
    }

    [Fact]
    public void SaveLoad_reachabilityRoundTrip()
    {
        SessionCacheStore.SaveServers(ServersSample, "user-a");
        SessionCacheStore.SaveReachability(
            new Dictionary<string, ServerReachState>
            {
                ["s1"] = new(ServerReachStatus.Online, 42, 55),
                ["gone"] = new(ServerReachStatus.Online, 1, 2),
                ["checking"] = ServerReachState.Checking,
            },
            "user-a");

        var snap = SessionCacheStore.Load("user-a");
        Assert.NotNull(snap);
        Assert.True(snap.Reachability.TryGetValue("s1", out var s1));
        Assert.Equal(42, s1.LatencyMs);
        Assert.Equal(55, s1.TlsMs);
        Assert.False(snap.Reachability.ContainsKey("gone"));
        Assert.False(snap.Reachability.ContainsKey("checking"));
    }

    [Fact]
    public void ReachState_encodeSkipsBareChecking_keepsCheckingWithMs()
    {
        var encoded = ServerReachState.EncodeCache(new Dictionary<string, ServerReachState>
        {
            ["a"] = ServerReachState.Checking,
            ["b"] = new(ServerReachStatus.Checking, 10, 12),
            ["c"] = ServerReachState.Offline,
        });
        var decoded = ServerReachState.DecodeCache(encoded);
        Assert.False(decoded.ContainsKey("a"));
        Assert.Equal(ServerReachStatus.Online, decoded["b"].Status);
        Assert.Equal(10, decoded["b"].LatencyMs);
        Assert.Equal(ServerReachStatus.Offline, decoded["c"].Status);
    }
}
