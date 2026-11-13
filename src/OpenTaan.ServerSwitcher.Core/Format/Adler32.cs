namespace OpenTaan.ServerSwitcher.Core.Format;

/// <summary>
/// The checksum stored in the .0m inner header.
/// </summary>
/// <remarks>
/// This is Adler-32 arithmetic, but it starts from <c>0</c> instead of the standard initial
/// value <c>1</c>. The result therefore differs from <c>zlib.adler32</c>; do not swap in a library
/// implementation.
/// </remarks>
public static class Adler32
{
    private const uint Mod = 65521;

    // Largest n such that 255*n*(n+1)/2 + (n+1)*(Mod-1) fits in 32 bits (same bound zlib uses).
    private const int MaxBlock = 5552;

    public static uint Compute(ReadOnlySpan<byte> data, uint initial = 0)
    {
        uint s1 = initial & 0xFFFF;
        uint s2 = (initial >> 16) & 0xFFFF;

        while (data.Length > 0)
        {
            var block = data[..Math.Min(MaxBlock, data.Length)];
            foreach (var b in block)
            {
                s1 += b;
                s2 += s1;
            }

            s1 %= Mod;
            s2 %= Mod;
            data = data[block.Length..];
        }

        return (s2 << 16) | s1;
    }
}
