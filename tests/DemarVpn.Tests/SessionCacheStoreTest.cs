using System.Text.Json.Nodes;
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
}
