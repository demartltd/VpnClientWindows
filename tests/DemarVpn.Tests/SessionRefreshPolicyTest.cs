using DemarVpn.Services.Servers;

namespace DemarVpn.Tests;

public class SessionRefreshPolicyTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    [Fact]
    public void Nil_isStale() => Assert.True(SessionRefreshPolicy.IsStale(null, Now));

    [Fact]
    public void FreshWithinTtl() =>
        Assert.False(SessionRefreshPolicy.IsStale(Now.AddSeconds(-599), Now));

    [Fact]
    public void StaleAtTtl() =>
        Assert.True(SessionRefreshPolicy.IsStale(Now.AddSeconds(-600), Now));

    [Fact]
    public void StaleBeyondTtl() =>
        Assert.True(SessionRefreshPolicy.IsStale(Now.AddSeconds(-900), Now));
}
