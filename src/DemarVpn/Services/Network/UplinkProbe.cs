using System.Net.Sockets;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Network;

public static class UplinkProbe
{
    private const int DefaultTcpMs = 3_000;

    public static async Task<bool> TcpReachableAsync(
        string host, int port, int timeoutMs = DefaultTcpMs, CancellationToken cancellationToken = default)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            await socket.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task WaitForDiagPortClosedAsync(int budgetMs = ConnectTimeouts.DiagPortCloseMs)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await TcpReachableAsync(SingBoxTags.DiagListenHost, SingBoxTags.DiagListenPort, 200))
                return;
            await Task.Delay(100);
        }
    }
}
