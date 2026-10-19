using DemarVpn.Services.Account;

namespace DemarVpn.Tests;

public class SubscriptionAlertStoreTest
{
    [Fact]
    public void ShouldShowExpiry_earlyAndFinalWindows()
    {
        var now = DateTimeOffset.Parse("2026-10-08T12:00:00Z");
        // 48h — early window (24h, 72h]
        Assert.True(SubscriptionAlertStore.ShouldShowExpiry("2026-10-10T12:00:00Z", now));
        // 72h — edge of early window
        Assert.True(SubscriptionAlertStore.ShouldShowExpiry("2026-10-11T12:00:00Z", now));
        // 73h — too early
        Assert.False(SubscriptionAlertStore.ShouldShowExpiry("2026-10-11T13:00:00Z", now));
        // 25h — early window
        Assert.True(SubscriptionAlertStore.ShouldShowExpiry("2026-10-09T13:00:00Z", now));
        // 24h — silent gap (18h, 24h]
        Assert.False(SubscriptionAlertStore.ShouldShowExpiry("2026-10-09T12:00:00Z", now));
        // 20h — silent gap
        Assert.False(SubscriptionAlertStore.ShouldShowExpiry("2026-10-09T08:00:00Z", now));
        // 18h — final window
        Assert.True(SubscriptionAlertStore.ShouldShowExpiry("2026-10-09T06:00:00Z", now));
        // 12h — final window
        Assert.True(SubscriptionAlertStore.ShouldShowExpiry("2026-10-09T00:00:00Z", now));
        // already expired
        Assert.False(SubscriptionAlertStore.ShouldShowExpiry("2026-10-08T11:00:00Z", now));
    }

    [Fact]
    public void ShouldShowTraffic_usedAtLeast200MiB()
    {
        var threshold = 200L * 1024 * 1024;
        Assert.False(SubscriptionAlertStore.ShouldShowTraffic(threshold - 1));
        Assert.True(SubscriptionAlertStore.ShouldShowTraffic(threshold));
        Assert.True(SubscriptionAlertStore.ShouldShowTraffic(threshold + 1));
    }

    [Fact]
    public void RemainingTime_hoursOrMinutes()
    {
        var hours = SubscriptionAlertStore.RemainingTime(5L * 60 * 60 * 1000);
        Assert.True(hours.Hours);
        Assert.Equal(5, hours.Amount);

        var minutes = SubscriptionAlertStore.RemainingTime(45L * 60 * 1000);
        Assert.False(minutes.Hours);
        Assert.Equal(45, minutes.Amount);

        var edge = SubscriptionAlertStore.RemainingTime(59L * 60 * 1000);
        Assert.False(edge.Hours);
        Assert.Equal(59, edge.Amount);
    }
}
