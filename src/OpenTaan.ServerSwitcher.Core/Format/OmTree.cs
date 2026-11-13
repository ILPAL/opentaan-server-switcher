using System.Buffers.Binary;
using System.Text;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Format;

/// <summary>
/// The decrypted plaintext of a .0m file: a 14-byte header followed by one serialized node tree
/// Strings are <c>u32 charCount</c> + UTF-16LE characters.
/// <code>
/// node = string name · string metadata
///        u32 attributeCount · (string key · string value) * attributeCount
///        u32 childCount · node * childCount
/// </code>
/// </summary>
/// <param name="Header">The 14 header bytes, kept verbatim (bytes 10–11 hold 0x27AA; the rest is not understood).</param>
/// <param name="Root">The root node.</param>
/// <param name="Trailer">Any bytes after the root node, kept so writing is lossless. Normally empty.</param>
public sealed record OmTree(byte[] Header, ConfigNode Root, byte[] Trailer)
{
    public const int HeaderSize = 14;
    private const int HeaderMagicOffset = 10;
    private const int MaxDepth = 512;

    /// <summary>Tree header found in the stock taan_server.0m; used when building a file from scratch.</summary>
    public static byte[] DefaultHeader =>
        [0xAA, 0x47, 0x7F, 0x04, 0xD5, 0x1A, 0x00, 0x00, 0xE8, 0xF1, 0xAA, 0x27, 0x00, 0x00];

    public static OmTree Read(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length < HeaderSize)
        {
            throw new OmFormatException($"Decrypted data is too small ({plaintext.Length} bytes) to contain a config tree.");
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(plaintext[HeaderMagicOffset..]);
        if (magic != OmContainer.Magic)
        {
            throw new OmFormatException($"Decrypted data has tree magic 0x{magic:X4}, expected 0x{OmContainer.Magic:X4}.");
        }

        var reader = new Reader(plaintext, HeaderSize);
        var root = reader.ReadNode(0);
        return new OmTree(plaintext[..HeaderSize].ToArray(), root, plaintext[reader.Position..].ToArray());
    }

    public byte[] Write() => Write(Header, Root, Trailer);

    public static byte[] Write(ReadOnlySpan<byte> header, ConfigNode root, ReadOnlySpan<byte> trailer = default)
    {
        if (header.Length != HeaderSize)
        {
            throw new ArgumentException($"Tree header must be {HeaderSize} bytes.", nameof(header));
        }

        using var stream = new MemoryStream();
        stream.Write(header);
        WriteNode(stream, root);
        stream.Write(trailer);
        return stream.ToArray();
    }

    private static void WriteNode(Stream stream, ConfigNode node)
    {
        WriteString(stream, node.Name);
        WriteString(stream, node.Metadata);
        WriteUInt32(stream, (uint)node.Attributes.Count);
        foreach (var attribute in node.Attributes)
        {
            WriteString(stream, attribute.Key);
            WriteString(stream, attribute.Value);
        }

        WriteUInt32(stream, (uint)node.Children.Count);
        foreach (var child in node.Children)
        {
            WriteNode(stream, child);
        }
    }

    private static void WriteString(Stream stream, string text)
    {
        WriteUInt32(stream, (uint)text.Length);
        stream.Write(Encoding.Unicode.GetBytes(text));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;

        public Reader(ReadOnlySpan<byte> data, int position)
        {
            _data = data;
            Position = position;
        }

        public int Position { get; private set; }

        private readonly int Remaining => _data.Length - Position;

        public ConfigNode ReadNode(int depth)
        {
            if (depth > MaxDepth)
            {
                throw new OmFormatException($"Config tree is nested deeper than {MaxDepth} levels.");
            }

            var start = Position;
            var node = new ConfigNode(ReadString("node name"), ReadString("node metadata"));

            // Each attribute needs at least 8 bytes (two empty strings); reject absurd counts early.
            var attributeCount = ReadCount("attribute", minBytesPerItem: 8, start);
            for (var i = 0; i < attributeCount; i++)
            {
                node.AddAttribute(ReadString("attribute key"), ReadString("attribute value"));
            }

            // Each child needs at least 16 bytes (two empty strings and two zero counts).
            var childCount = ReadCount("child", minBytesPerItem: 16, start);
            for (var i = 0; i < childCount; i++)
            {
                node.AddChild(ReadNode(depth + 1));
            }

            return node;
        }

        private int ReadCount(string what, int minBytesPerItem, int nodeStart)
        {
            var count = ReadUInt32(what + " count");
            if (count > (uint)(Remaining / minBytesPerItem))
            {
                throw new OmFormatException(
                    $"Corrupt config tree: node at offset 0x{nodeStart:X} declares {count} {what}(ren) but only {Remaining} bytes remain.");
            }

            return (int)count;
        }

        private string ReadString(string what)
        {
            var at = Position;
            var length = ReadUInt32(what + " length");
            if (length > (uint)(Remaining / 2))
            {
                throw new OmFormatException(
                    $"Corrupt config tree: {what} at offset 0x{at:X} claims {length} characters but only {Remaining} bytes remain.");
            }

            var byteCount = (int)length * 2;
            var text = Encoding.Unicode.GetString(_data.Slice(Position, byteCount));
            Position += byteCount;
            return text;
        }

        private uint ReadUInt32(string what)
        {
            if (Remaining < 4)
            {
                throw new OmFormatException($"Corrupt config tree: unexpected end of data while reading {what} at offset 0x{Position:X}.");
            }

            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
            Position += 4;
            return value;
        }
    }
}
