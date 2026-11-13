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

    [Fact]
    public void Save_EndsUndoMerge_SoUndoStopsAtTheSavedValue()
    {
        using var temp = new Fixtures.TempDirectory();
        var doc = OpenSample(temp);
        var channel = ServerEndpoints.FindChannels(doc.Root)[0];
        var ip = channel.FindAttribute("ip")!;

        doc.Execute(new SetAttributeValueCommand(channel, ip, "1.2.3.4"));
        doc.Save();
        doc.Execute(new SetAttributeValueCommand(channel, ip, "1.2.3.45"));
        doc.History.Undo();

        Assert.Equal("1.2.3.4", ip.Value);
        Assert.False(doc.IsModified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RevertRemovedAttributes_InAnyOrder_RestoresOriginalOrder(bool laterFirst)
    {
        var doc = new ConfigDocument(OmFile.Decode(SampleBytes()));
        var channel = ServerEndpoints.FindChannels(doc.Root)[0];
        doc.Execute(new RemoveAttributeCommand(channel, channel.FindAttribute("name")!));
        doc.Execute(new RemoveAttributeCommand(channel, channel.FindAttribute("ip")!));

        foreach (var key in laterFirst ? new[] { "ip", "name" } : ["name", "ip"])
        {
            var change = doc.GetChanges().Single(c => c.Kind == ChangeKind.AttributeRemoved && c.Key == key);
            doc.Execute(change.CreateRevertCommand()!);
        }

        Assert.Equal(["name", "ip", "port"], channel.Attributes.Select(a => a.Key));
        Assert.Empty(doc.GetChanges());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RevertRemovedElements_InAnyOrder_RestoresOriginalOrder(bool laterFirst)
    {
        var doc = new ConfigDocument(OmFile.Decode(SampleBytes()));
        var server = doc.Root.Children[0];
        doc.Execute(new RemoveNodeCommand(server.Children[0])); // ch-1
        doc.Execute(new RemoveNodeCommand(server.Children[0])); // ch-2

        for (var i = 0; i < 2; i++)
        {
            var removed = doc.GetChanges().Where(c => c.Kind == ChangeKind.ElementRemoved).ToList();
            var change = laterFirst ? removed[^1] : removed[0];
            doc.Execute(change.CreateRevertCommand()!);
        }

        Assert.Equal(["ch-1", "ch-2", "ch-3"], server.Children.Select(c => c.GetValue("name")));
        Assert.Empty(doc.GetChanges());
    }

    [Fact]
    public void SwitchServer_MatchesIpAndPortKeysIgnoringCase()
    {
        var root = new ConfigNode("content");
        var server = root.AddChild(new ConfigNode("server"));
        var upper = server.AddChild(new ConfigNode("Channel"));
        upper.AddAttribute("IP", "1.1.1.1");
        upper.AddAttribute("Port", "2180");
        var mixed = server.AddChild(new ConfigNode("channel"));
        mixed.AddAttribute("ip", "1.1.1.1");
        mixed.AddAttribute("Port", "2180");

        ServerEndpoints.CreateSwitchCommand(root, "10.0.0.9", "3000")!.Do();

        Assert.Equal(2, ServerEndpoints.FindChannels(root).Count);
        foreach (var channel in new[] { upper, mixed })
        {
            Assert.Equal(2, channel.Attributes.Count); // no second, lowercase port added
            Assert.Equal("10.0.0.9", ServerEndpoints.GetIp(channel));
            Assert.Equal("3000", ServerEndpoints.GetPort(channel));
        }
    }

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
