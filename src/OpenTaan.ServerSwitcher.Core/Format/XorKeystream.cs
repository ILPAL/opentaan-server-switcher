using System.Buffers.Binary;

namespace OpenTaan.ServerSwitcher.Core.Format;

/// <summary>
/// The repeating 64-byte XOR key derived from the per-file seed.
/// XOR is symmetric, so the same call encrypts and decrypts.
/// </summary>
public static class XorKeystream
{
    public const int KeyLength = 64;
    private const uint SeedMask = 0x8473FBC1;
    private const uint Step = 0x7B8C043F;

    /// <summary>16 little-endian words: w0 = seed ^ 0x8473FBC1, w(n) = w(n-1) - 0x7B8C043F.</summary>
    public static byte[] GenerateKey(uint seed)
    {
        var key = new byte[KeyLength];
        var word = seed ^ SeedMask;
        for (var i = 0; i < KeyLength / 4; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(i * 4), word);
            unchecked
            {
                word -= Step;
            }
        }

        return key;
    }

    /// <summary>XORs <paramref name="data"/> in place with the keystream for <paramref name="seed"/>.</summary>
    public static void Apply(Span<byte> data, uint seed)
    {
        var key = GenerateKey(seed);
        for (var i = 0; i < data.Length; i++)
        {
            data[i] ^= key[i % KeyLength];
        }
    }
}
