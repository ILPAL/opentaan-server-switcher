using System.Buffers.Binary;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Tests;

/// <summary>Regression tests for bugs found in code review. They use the in-code sample tree, not the stock file.</summary>
public class RegressionTests
{
    private static byte[] SampleBytes(uint? seed = null)
    {
        var container = seed is null ? OmContainerInfo.TaanDefault : OmContainerInfo.TaanDefault with { Seed = seed };
        return OmFile.Encode(container, OmTree.DefaultHeader, Fixtures.SampleTree());
    }

    private static ConfigDocument OpenSample(Fixtures.TempDirectory temp, string name = "taan_server.0m")
    {
        var path = temp.File(name);
        File.WriteAllBytes(path, SampleBytes());
        return ConfigDocument.Open(path);
    }

    private static string? FirstIp(string path) =>
        ServerEndpoints.FindChannels(OmFile.Load(path).Tree.Root)[0].GetValue("ip");

    [Theory]
    [InlineData(0u)]
    [InlineData(5u)]
    public void Decode_DeclaredInnerSizeTooSmall_ThrowsFormatException(uint declaredSize)
    {
        var bytes = SampleBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), declaredSize);

        Assert.Throws<OmFormatException>(() => OmContainer.Decode(bytes));
    }
}
