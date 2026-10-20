namespace DemarVpn.Services.Network;

/// <summary>Hysteria2 salamander UDP obfuscation (sing-quic): salt(8) + XOR(blake2b-256(password‖salt)).</summary>
public static class SalamanderObfs
{
    public const int SaltLen = 8;

    public static byte[] Wrap(ReadOnlySpan<byte> payload, string password)
    {
        Span<byte> salt = stackalloc byte[SaltLen];
        Random.Shared.NextBytes(salt);
        Span<byte> msg = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount(password) + SaltLen];
        var n = System.Text.Encoding.UTF8.GetBytes(password, msg);
        salt.CopyTo(msg[n..]);
        var key = Blake2b256(msg[..(n + SaltLen)]);
        var outBuf = new byte[SaltLen + payload.Length];
        salt.CopyTo(outBuf);
        for (var i = 0; i < payload.Length; i++)
            outBuf[SaltLen + i] = (byte)(payload[i] ^ key[i % 32]);
        return outBuf;
    }

    /// <summary>RFC 7693 BLAKE2b-256, digest only (no key).</summary>
    public static byte[] Blake2b256(ReadOnlySpan<byte> message)
    {
        Span<ulong> h = stackalloc ulong[8];
        Blake2b.Iv.CopyTo(h);
        h[0] ^= 0x01010000u ^ 32;
        ulong t = 0;
        var offset = 0;
        var n = message.Length;
        while (offset + 128 <= n)
        {
            t += 128;
            Blake2b.Compress(h, message.Slice(offset, 128), t, last: false);
            offset += 128;
        }
        Span<byte> block = stackalloc byte[128];
        block.Clear();
        var rem = n - offset;
        if (rem > 0) message.Slice(offset, rem).CopyTo(block);
        t += (ulong)rem;
        Blake2b.Compress(h, block, t, last: true);
        var outBuf = new byte[32];
        for (var i = 0; i < 4; i++)
        {
            var w = h[i];
            for (var j = 0; j < 8; j++)
            {
                outBuf[i * 8 + j] = (byte)(w & 0xff);
                w >>= 8;
            }
        }
        return outBuf;
    }
}

file static class Blake2b
{
    internal static readonly ulong[] Iv =
    [
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
    ];

    private static readonly int[][] Sigma =
    [
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
        [11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4],
        [7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8],
        [9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13],
        [2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9],
        [12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11],
        [13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10],
        [6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5],
        [10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0],
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
    ];

    internal static void Compress(Span<ulong> h, ReadOnlySpan<byte> block, ulong t, bool last)
    {
        Span<ulong> m = stackalloc ulong[16];
        for (var i = 0; i < 16; i++)
        {
            var j = i * 8;
            m[i] = block[j] | (ulong)block[j + 1] << 8 | (ulong)block[j + 2] << 16
                | (ulong)block[j + 3] << 24 | (ulong)block[j + 4] << 32 | (ulong)block[j + 5] << 40
                | (ulong)block[j + 6] << 48 | (ulong)block[j + 7] << 56;
        }
        Span<ulong> v = stackalloc ulong[16];
        h.CopyTo(v);
        Iv.CopyTo(v[8..]);
        v[12] ^= t;
        if (last) v[14] = ~v[14];
        for (var r = 0; r < 12; r++)
        {
            var s = Sigma[r];
            G(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            G(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            G(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            G(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            G(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            G(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            G(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            G(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }
        for (var i = 0; i < 8; i++) h[i] ^= v[i] ^ v[i + 8];
    }

    private static void G(Span<ulong> v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] = v[a] + v[b] + x;
        v[d] = Rot(v[d] ^ v[a], 32);
        v[c] = v[c] + v[d];
        v[b] = Rot(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y;
        v[d] = Rot(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d];
        v[b] = Rot(v[b] ^ v[c], 63);
    }

    private static ulong Rot(ulong x, int n) => (x >> n) | (x << (64 - n));
}
