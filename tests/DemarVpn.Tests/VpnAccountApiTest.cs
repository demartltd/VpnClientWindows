using System.Text.Json.Nodes;
using DemarVpn.Services.Account;
using Xunit;

namespace DemarVpn.Tests;

public class VpnAccountApiTest
{
    [Fact]
    public void ParseAccount_readsNotifyAndSubscription()
    {
        var json = JsonNode.Parse(
            """
            {
              "notifyNews": false,
              "notifyImportant": true,
              "vpnSubscription": "plus",
              "vpnExpiresAt": "2026-12-31T00:00:00.000Z"
            }
            """) as JsonObject;
        var account = VpnAccountApi.ParseAccount(json!);
        Assert.False(account.NotifyNews);
        Assert.True(account.NotifyImportant);
        Assert.Equal("plus", account.VpnSubscription);
        Assert.Equal("2026-12-31T00:00:00.000Z", account.VpnExpiresAt);
    }

    [Fact]
    public void ParseTraffic_sumsServers()
    {
        var json = JsonNode.Parse(
            """
            {
              "y": 2026,
              "m": 8,
              "servers": [
                { "name": "DE-VIP-3", "bytes": 5000 },
                { "name": "FI-FREE-1", "bytes": 1500 }
              ],
              "total": 6500,
              "limitBytes": 21474836480
            }
            """) as JsonObject;
        var traffic = VpnAccountApi.ParseTraffic(json!);
        Assert.Equal(2026, traffic.Y);
        Assert.Equal(8, traffic.M);
        Assert.Equal(2, traffic.Servers.Count);
        Assert.Equal("DE-VIP-3", traffic.Servers[0].Name);
        Assert.Equal(5000L, traffic.Servers[0].Bytes);
        Assert.Equal(6500L, traffic.Total);
        Assert.Equal(21474836480L, traffic.LimitBytes);
    }

    [Fact]
    public void PieSlices_collapsesBeyondSix()
    {
        var servers = Enumerable.Range(1, 8)
            .Select(i => new VpnTrafficServer($"S{i}", (9 - i) * 100L))
            .ToList();
        var slices = VpnAccountApi.PieSlices(servers, "Other");
        Assert.Equal(6, slices.Count);
        Assert.Equal("Other", slices[^1].Name);
        Assert.Equal(300L + 200L + 100L, slices[^1].Bytes);
    }

    [Fact]
    public void FormatBytes_compact()
    {
        Assert.Equal("512 B", VpnAccountApi.FormatBytes(512));
        Assert.Equal("1.5 KB", VpnAccountApi.FormatBytes(1536));
        Assert.Equal("1.0 MB", VpnAccountApi.FormatBytes(1024L * 1024));
    }
}
