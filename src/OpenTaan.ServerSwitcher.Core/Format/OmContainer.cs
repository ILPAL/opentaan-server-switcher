using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenTaan.ServerSwitcher.Core.Format;

/// <summary>
/// Header values of a .0m container. Used both to describe a file that was read and as the template
/// when writing it back, so the seed and flags of the original file are preserved.
/// </summary>
/// <param name="OuterFlags">u16 at offset 2 of the file (0 in all known files).</param>
/// <param name="InnerFlags">Bit 0: decompressed size present (payload is zlib). Bit 1: seed present (payload is XOR-encrypted).</param>
/// <param name="Seed">XOR keystream seed, or null when the payload is not encrypted.</param>
/// <param name="Checksum">Adler-32 variant (initial 0) of the plaintext, as stored in the file.</param>
/// <param name="DecompressedSize">Plaintext length as stored in the file, or null when not compressed.</param>
/// <param name="HeaderBytes">The raw header bytes (outer + inner, up to the payload), for display.</param>
public sealed record OmContainerInfo(
    ushort OuterFlags,
    byte InnerFlags,
    uint? Seed,
    uint Checksum,
    uint? DecompressedSize,
    byte[] HeaderBytes)
{
    public bool IsCompressed => (InnerFlags & OmContainer.FlagCompressed) != 0;

    public bool IsEncrypted => (InnerFlags & OmContainer.FlagEncrypted) != 0;

    /// <summary>Container settings of the stock taan_server.0m: zlib + XOR with seed 0x83EF8226.</summary>
    public static OmContainerInfo TaanDefault { get; } =
        new(0, OmContainer.FlagCompressed | OmContainer.FlagEncrypted, OmContainer.TaanDefaultSeed, 0, null, []);
}

/// <summary>
/// The outer container of a .0m file:
/// <code>
/// u16 magic 0x27AA · u16 flags · u32 size of the inner block
/// u8 'S' · u8 flags · u32 checksum · [u32 seed if flags&amp;2] · [u32 plaintext size if flags&amp;1]
/// payload = XOR(zlib(plaintext))
/// </code>
/// </summary>
public static class OmContainer
{
    public const ushort Magic = 0x27AA;
    public const byte Signature = 0x53; // 'S'
    public const byte FlagCompressed = 0x01;
    public const byte FlagEncrypted = 0x02;
    public const uint TaanDefaultSeed = 0x83EF8226;
    public const int OuterHeaderSize = 8;

    private const byte KnownFlags = FlagCompressed | FlagEncrypted;
    private const int MaxPlaintextSize = 64 * 1024 * 1024;

    public sealed record DecodeResult(OmContainerInfo Info, byte[] Plaintext, IReadOnlyList<string> Warnings);

    public static DecodeResult Decode(ReadOnlySpan<byte> file)
    {
        var warnings = new List<string>();

        if (file.Length < OuterHeaderSize + 6)
        {
            throw new OmFormatException($"File is too small to be a .0m file ({file.Length} bytes).");
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(file);
        if (magic != Magic)
        {
            throw new OmFormatException($"Not a .0m file: magic is 0x{magic:X4}, expected 0x{Magic:X4}.");
        }

        var outerFlags = BinaryPrimitives.ReadUInt16LittleEndian(file[2..]);
        var declaredInnerSize = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]);
        var inner = file[OuterHeaderSize..];
        if (declaredInnerSize != (uint)inner.Length)
        {
            if (declaredInnerSize > (uint)inner.Length)
            {
                throw new OmFormatException(
                    $"File is truncated: header declares {declaredInnerSize} bytes of data but only {inner.Length} follow.");
            }

            warnings.Add($"Header declares {declaredInnerSize} bytes of data but {inner.Length} follow; extra bytes are ignored.");
            inner = inner[..(int)declaredInnerSize];
        }

        if (inner[0] != Signature)
        {
            throw new OmFormatException($"Unsupported .0m variant: signature is 0x{inner[0]:X2}, expected 'S' (0x53).");
        }

        var flags = inner[1];
        if ((flags & ~KnownFlags) != 0)
        {
            throw new OmFormatException($"Unsupported .0m flags 0x{flags:X2}.");
        }

        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(inner[2..]);
        var offset = 6;

        uint? seed = null;
        if ((flags & FlagEncrypted) != 0)
        {
            seed = ReadField(inner, ref offset, "seed");
        }

        uint? size = null;
        if ((flags & FlagCompressed) != 0)
        {
            size = ReadField(inner, ref offset, "decompressed size");
            if (size > MaxPlaintextSize)
            {
                throw new OmFormatException($"Declared decompressed size {size} is unreasonably large.");
            }
        }

        var headerBytes = file[..(OuterHeaderSize + offset)].ToArray();
        var payload = inner[offset..].ToArray();

        if (seed is { } s)
        {
            XorKeystream.Apply(payload, s);
        }

        byte[] plaintext;
        if (size is { } expected)
        {
            plaintext = Decompress(payload);
            if (plaintext.Length != expected)
            {
                warnings.Add($"Header declares {expected} decompressed bytes but the data has {plaintext.Length}.");
            }
        }
        else
        {
            plaintext = payload;
        }

        var actual = Adler32.Compute(plaintext);
        if (actual != checksum)
        {
            throw new OmFormatException(
                $"Checksum mismatch (file says 0x{checksum:X8}, data gives 0x{actual:X8}). The file is damaged or uses a different key.");
        }

        var info = new OmContainerInfo(outerFlags, flags, seed, checksum, size, headerBytes);
        return new DecodeResult(info, plaintext, warnings);
    }

    /// <summary>
    /// Builds a .0m file from <paramref name="plaintext"/>, reusing the flags and seed of
    /// <paramref name="template"/>. The checksum and size fields are recomputed.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> plaintext, OmContainerInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if ((template.InnerFlags & ~KnownFlags) != 0)
        {
            throw new ArgumentException($"Unsupported flags 0x{template.InnerFlags:X2}.", nameof(template));
        }

        if (template.IsEncrypted && template.Seed is null)
        {
            throw new ArgumentException("Template says the payload is encrypted but has no seed.", nameof(template));
        }

        var payload = template.IsCompressed ? Compress(plaintext) : plaintext.ToArray();
        if (template.IsEncrypted)
        {
            XorKeystream.Apply(payload, template.Seed!.Value);
        }

        var innerHeaderSize = 6 + (template.IsEncrypted ? 4 : 0) + (template.IsCompressed ? 4 : 0);
        var innerSize = innerHeaderSize + payload.Length;
        var result = new byte[OuterHeaderSize + innerSize];
        var span = result.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], template.OuterFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)innerSize);

        var inner = span[OuterHeaderSize..];
        inner[0] = Signature;
        inner[1] = template.InnerFlags;
        BinaryPrimitives.WriteUInt32LittleEndian(inner[2..], Adler32.Compute(plaintext));
        var offset = 6;
        if (template.IsEncrypted)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(inner[offset..], template.Seed!.Value);
            offset += 4;
        }

        if (template.IsCompressed)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(inner[offset..], (uint)plaintext.Length);
            offset += 4;
        }

        payload.CopyTo(inner[offset..]);
        return result;
    }

    private static uint ReadField(ReadOnlySpan<byte> inner, ref int offset, string what)
    {
        if (inner.Length < offset + 4)
        {
            throw new OmFormatException($"File is truncated: missing the {what} field.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(inner[offset..]);
        offset += 4;
        return value;
    }

    private static byte[] Decompress(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = z.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxPlaintextSize)
                {
                    throw new OmFormatException("Decompressed data is unreasonably large.");
                }
            }

            return output.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new OmFormatException("Payload is not valid zlib data after decryption (wrong seed or damaged file).", ex);
        }
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(data);
        }

        return output.ToArray();
    }
}
