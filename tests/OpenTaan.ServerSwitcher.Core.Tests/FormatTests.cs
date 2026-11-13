using System.Buffers.Binary;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Tests;

public class FormatTests
{
    [Fact]
    public void Keystream_MatchesReferenceKey()
    {
        var key = XorKeystream.GenerateKey(Fixtures.TaanServerSeed);
        Assert.Equal(64, key.Length);
        Assert.Equal(
            "e7799c07a875108c697184102a6df894eb686c19ac64e09d6d6054222e5cc8a6ef573c2bb053b0af714f2434324b98b8f3460c3db44280c1753ef445363a68ca",
            Convert.ToHexStringLower(key));
    }

    [Fact]
    public void Xor_IsItsOwnInverse()
    {
        var data = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        var copy = data.ToArray();
        XorKeystream.Apply(copy, 12345);
        Assert.NotEqual(data, copy);
        XorKeystream.Apply(copy, 12345);
        Assert.Equal(data, copy);
    }

    [Fact]
    public void Adler32_StartsFromZero()
    {
        Assert.Equal(0u, Adler32.Compute([]));
        // Standard Adler-32("a") is 0x00620062; with initial s1 = 0 the .0m checksum is 0x00610061.
        Assert.Equal(0x00610061u, Adler32.Compute("a"u8));
    }

    [Fact]
    public void Adler32_LargeInputMatchesNaiveImplementation()
    {
        var data = new byte[100_000];
        new Random(42).NextBytes(data);
        uint s1 = 0, s2 = 0;
        foreach (var b in data)
        {
            s1 = (s1 + b) % 65521;
            s2 = (s2 + s1) % 65521;
        }

        Assert.Equal((s2 << 16) | s1, Adler32.Compute(data));
    }

    [StockFileFact]
    public void Decode_StockFile_ReadsHeadersAndVerifiesChecksum()
    {
        var result = OmContainer.Decode(Fixtures.TaanServer);

        Assert.Equal(Fixtures.TaanServerSeed, result.Info.Seed);
        Assert.Equal(Fixtures.TaanServerChecksum, result.Info.Checksum);
        Assert.Equal((uint)Fixtures.TaanServerPlaintextLength, result.Info.DecompressedSize);
        Assert.Equal(0x03, result.Info.InnerFlags);
        Assert.Equal(0, result.Info.OuterFlags);
        Assert.Equal(22, result.Info.HeaderBytes.Length);
        Assert.Empty(result.Warnings);
        Assert.Equal(Fixtures.TaanServerPlaintextSha256, Fixtures.Sha256(result.Plaintext));
    }

    [StockFileFact]
    public void Decode_StockFile_ParsesExpectedTree()
    {
        var content = OmFile.Decode(Fixtures.TaanServer);
        var root = content.Tree.Root;

        Assert.True(content.RoundTripsExactly);
        Assert.Equal("content", root.Name);
        Assert.Equal(
            ["server", "EnableGuildPoint", "EnableRanking", "EnableSoul", "ShopCategoryPendent", "ShopCategoryInstant"],
            root.Children.Select(c => c.Name));

        var server = root.Children[0];
        Assert.Equal("Vietnam-Service", server.GetValue("name"));

        var channels = ServerEndpoints.FindChannels(root);
        Assert.Equal(6, channels.Count);
        Assert.Equal(["ch-1", "ch-2", "ch-3", "ch-4", "ch-5", "ch-6"], channels.Select(c => c.GetValue("name")));
        Assert.Equal(Fixtures.OriginalIps, channels.Select(c => c.GetValue("ip")));
        Assert.All(channels, c => Assert.Equal(Fixtures.OriginalPort, c.GetValue("port")));
    }

    [StockFileFact]
    public void Tree_WriteOfParsedTree_IsByteIdentical()
    {
        var plaintext = OmContainer.Decode(Fixtures.TaanServer).Plaintext;
        var tree = OmTree.Read(plaintext);
        Assert.Equal(plaintext, tree.Write());
        Assert.Equal(OmTree.DefaultHeader, tree.Header);
        Assert.Empty(tree.Trailer);
    }

    [StockFileFact]
    public void Encode_StockFile_KeepsSeedFlagsAndHeaderLayout()
    {
        var original = Fixtures.TaanServer;
        var content = OmFile.Decode(original);
        var encoded = OmFile.Encode(content.Container, content.Tree.Header, content.Tree.Root, content.Tree.Trailer);

        // Header fields up to the payload (magic, flags, size, 'S', flags, checksum, seed, plaintext size).
        // The inner-size field may differ if the compressor produces a different (equally valid) zlib stream.
        var again = OmContainer.Decode(encoded);
        Assert.Equal(Fixtures.TaanServerSeed, again.Info.Seed);
        Assert.Equal(content.Container.InnerFlags, again.Info.InnerFlags);
        Assert.Equal(content.Container.OuterFlags, again.Info.OuterFlags);
        Assert.Equal(Fixtures.TaanServerChecksum, again.Info.Checksum);
        Assert.Equal(content.Plaintext, again.Plaintext);
        Assert.Equal(original.AsSpan(8, 14).ToArray(), encoded.AsSpan(8, 14).ToArray()); // 'S', flags, checksum, seed, size
    }

    [StockFileFact]
    public void Encode_UnchangedStockFile_IsByteIdenticalToOriginal()
    {
        // .NET's zlib (SmallestSize) produces the same stream as the zlib level 9 used for the stock file.
        // Not required for correctness, but it proves nothing but the edited values ever changes.
        var original = Fixtures.TaanServer;
        var content = OmFile.Decode(original);
        Assert.Equal(original, OmFile.Encode(content.Container, content.Tree.Header, content.Tree.Root, content.Tree.Trailer));
    }

    [Fact]
    public void Encode_UnencryptedUncompressedVariant_RoundTrips()
    {
        var plaintext = OmTree.Write(OmTree.DefaultHeader, Fixtures.SampleTree());
        var template = new OmContainerInfo(0, 0, null, 0, null, []);
        var encoded = OmContainer.Encode(plaintext, template);
        var decoded = OmContainer.Decode(encoded);
        Assert.Equal(plaintext, decoded.Plaintext);
        Assert.Null(decoded.Info.Seed);
        Assert.Equal(14, decoded.Info.HeaderBytes.Length);
    }

    [StockFileFact]
    public void Decode_RejectsWrongMagic()
    {
        var bytes = Fixtures.TaanServer;
        bytes[0] = 0x00;
        var ex = Assert.Throws<OmFormatException>(() => OmContainer.Decode(bytes));
        Assert.Contains("magic", ex.Message);
    }

    [StockFileFact]
    public void Decode_RejectsCorruptedPayload()
    {
        var bytes = Fixtures.TaanServer;
        bytes[^10] ^= 0xFF;
        Assert.Throws<OmFormatException>(() => OmContainer.Decode(bytes));
    }

    [StockFileFact]
    public void Decode_RejectsWrongSeed()
    {
        var bytes = Fixtures.TaanServer;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 0x12345678);
        Assert.Throws<OmFormatException>(() => OmContainer.Decode(bytes));
    }

    [StockFileFact]
    public void Decode_RejectsTruncatedFile()
    {
        var bytes = Fixtures.TaanServer[..100];
        var ex = Assert.Throws<OmFormatException>(() => OmContainer.Decode(bytes));
        Assert.Contains("truncated", ex.Message);
    }

    [Fact]
    public void TreeRead_RejectsAbsurdCounts()
    {
        var plaintext = OmTree.Write(OmTree.DefaultHeader, new ConfigNode("root"));
        // Root: name(4+8) metadata(4) then attribute count at offset 14 + 12 + 4.
        BinaryPrimitives.WriteUInt32LittleEndian(plaintext.AsSpan(14 + 12 + 4), 0x7FFFFFFF);
        var ex = Assert.Throws<OmFormatException>(() => OmTree.Read(plaintext));
        Assert.Contains("attribute", ex.Message);
    }

    [Fact]
    public void TreeRead_RejectsTruncatedString()
    {
        var plaintext = OmTree.Write(OmTree.DefaultHeader, Fixtures.SampleTree());
        Assert.Throws<OmFormatException>(() => OmTree.Read(plaintext.AsSpan(0, plaintext.Length - 5)));
    }

    [Fact]
    public void TreeRead_KeepsTrailingBytes()
    {
        var plaintext = OmTree.Write(OmTree.DefaultHeader, Fixtures.SampleTree(), [1, 2, 3]);
        var tree = OmTree.Read(plaintext);
        Assert.Equal(new byte[] { 1, 2, 3 }, tree.Trailer);
        Assert.Equal(plaintext, tree.Write());
    }

    [Fact]
    public void Tree_RoundTripsUnicodeEmptyStringsAndDuplicateKeys()
    {
        var root = new ConfigNode("gốc", "meta ✓");
        root.AddAttribute("k", "Tiếng Việt 한국어");
        root.AddAttribute("k", "");
        root.AddAttribute("", "empty key");
        root.AddChild(new ConfigNode(""));

        var tree = OmTree.Read(OmTree.Write(OmTree.DefaultHeader, root));
        Assert.True(tree.Root.StructurallyEquals(root));
    }
}
