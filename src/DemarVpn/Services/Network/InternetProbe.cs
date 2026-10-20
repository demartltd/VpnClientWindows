using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;

namespace DemarVpn.Services.Network;

[Flags]
public enum ProbeKind
{
    Tcp = 1,
    Https = 2,
    Tls = 4,
    All = Tcp | Https | Tls,
}

/// <summary>TCP / TLS / HTTPS / QUIC latency probes (parity Android InternetProbe).</summary>
public static class InternetProbe
{
    public const int TimeoutMs = 2_000;
    public const int Attempts = 3;

    private static readonly HttpClient Http = CreateHttp();

    /// <summary>Selected kinds in parallel. Skipped kinds stay 0. Failures coerce to 0.</summary>
    public static async Task<EgressHostProbe> ProbeHostAsync(
        string host,
        int port = 443,
        ProbeKind kinds = ProbeKind.All,
        CancellationToken ct = default)
    {
        Task<int>? tcp = null, https = null, tls = null;
        if ((kinds & ProbeKind.Tcp) != 0)
            tcp = ProbeTcpMsAsync(host, port, ct);
        if ((kinds & ProbeKind.Https) != 0)
            https = ProbeHttpsMsAsync(host, ct);
        if ((kinds & ProbeKind.Tls) != 0)
            tls = ProbeTlsMsAsync(host, port, ct);

        static async Task<int> Coerce(Task<int>? t) =>
            t is null ? 0 : Math.Max(0, await t.ConfigureAwait(false));

        return new EgressHostProbe(
            await Coerce(tcp).ConfigureAwait(false),
            await Coerce(tls).ConfigureAwait(false),
            await Coerce(https).ConfigureAwait(false));
    }

    /// <summary>First success of ≤[Attempts] TCP connects. −1 if all fail. ConnectionRefused counts as RTT.</summary>
    public static Task<int> ProbeTcpMsAsync(
        string host, int port = 443, CancellationToken ct = default) =>
        FirstSuccessAsync($"tcp {host}:{port}", () => MeasureTcpOnceAsync(host, port, ct), ct);

    /// <summary>First success of ≤[Attempts] HTTPS GETs. −1 if all fail.</summary>
    public static Task<int> ProbeHttpsMsAsync(string host, CancellationToken ct = default) =>
        FirstSuccessAsync($"https {host}", () => MeasureHttpsOnceAsync(host, ct), ct);

    /// <summary>TLS handshake only (trust-any). −1 if all fail.</summary>
    public static Task<int> ProbeTlsMsAsync(
        string host, int port = 443, CancellationToken ct = default) =>
        FirstSuccessAsync($"tls {host}:{port}", () => MeasureTlsOnceAsync(host, port, ct), ct);

    /// <summary>QUIC VN RTT (RFC 9000 §6) for HY2. −1 if all fail.</summary>
    public static Task<int> ProbeQuicMsAsync(
        string host, int port = 443, CancellationToken ct = default) =>
        FirstSuccessAsync($"quic {host}:{port}", () => MeasureQuicOnceAsync(host, port, ct), ct);

    /// <summary>Long-header Initial, GREASE version 0x0a0a0a0a, padded to 1200 bytes.</summary>
    internal static byte[] QuicGreaseInitial()
    {
        var outBuf = new byte[1200];
        outBuf[0] = 0xc0;
        outBuf[1] = 0x0a;
        outBuf[2] = 0x0a;
        outBuf[3] = 0x0a;
        outBuf[4] = 0x0a;
        outBuf[5] = 8;
        Random.Shared.NextBytes(outBuf.AsSpan(6, 8));
        outBuf[14] = 8;
        Random.Shared.NextBytes(outBuf.AsSpan(15, 8));
        outBuf[23] = 0x00;
        return outBuf;
    }

    private static async Task<int> FirstSuccessAsync(
        string log, Func<Task<int>> attempt, CancellationToken ct)
    {
        for (var i = 0; i < Attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ms = await attempt().ConfigureAwait(false);
            if (ms >= 0)
            {
                AppLog.Append($"InternetProbe {log}={ms}ms (attempt {i + 1}/{Attempts})");
                return ms;
            }
        }
        AppLog.Append($"InternetProbe {log}=fail (attempts {Attempts})");
        return -1;
    }

    private static async Task<int> MeasureTcpOnceAsync(string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeoutMs);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return Elapsed(sw);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return -1;
        }
        catch (SocketException ex) when (IsRefusedOrReset(ex))
        {
            return Elapsed(sw);
        }
        catch
        {
            return -1;
        }
    }

    private static async Task<int> MeasureTlsOnceAsync(string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeoutMs);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            await using var ns = new NetworkStream(socket, ownsSocket: false);
            // Trust-any via constructor only — options callback throws if both set.
            await using var ssl = new SslStream(ns, leaveInnerStreamOpen: false, static (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = host },
                cts.Token).ConfigureAwait(false);
            return Elapsed(sw);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    private static async Task<int> MeasureHttpsOnceAsync(string host, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeoutMs);
            using var response = await Http.GetAsync($"https://{host}/", cts.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is < 200 or > 299) return -1;
            return Elapsed(sw);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    private static async Task<int> MeasureQuicOnceAsync(string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeoutMs);
            using var udp = new UdpClient();
            var pkt = QuicGreaseInitial();
            await udp.SendAsync(pkt, host, port, cts.Token).ConfigureAwait(false);
            await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            return Elapsed(sw);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    private static int Elapsed(Stopwatch sw) => Math.Max(1, (int)sw.ElapsedMilliseconds);

    private static bool IsRefusedOrReset(SocketException ex) =>
        ex.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset;

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TimeoutMs) };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        return client;
    }
}
