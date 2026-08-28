using DemarVpn.Services.Network;

namespace DemarVpn.Tests;

public class UplinkProbeTest
{
    [Fact]
    public async Task TcpReachable_closedPort_returnsFalse()
    {
        Assert.False(await UplinkProbe.TcpReachableAsync("127.0.0.1", 1, timeoutMs: 500));
    }
}
