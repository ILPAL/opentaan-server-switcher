using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Tests;

public class EditingTests
{
    private static ConfigDocument OpenStock() => new(OmFile.Decode(Fixtures.TaanServer));

    [StockFileFact]
    public void NewDocument_HasNoChanges()
    {
        var doc = OpenStock();
        Assert.Empty(doc.GetChanges());
        Assert.False(doc.IsModified);
        Assert.False(doc.History.CanUndo);
    }

    [StockFileFact]
    public void SwitchServer_ChangesEveryChannel_InOneUndoStep()
    {
        var doc = OpenStock();
        var command = ServerEndpoints.CreateSwitchCommand(doc.Root, "203.0.113.10", "2181");
        Assert.NotNull(command);
        doc.Execute(command);

        Assert.All(ServerEndpoints.FindChannels(doc.Root), c =>
        {
            Assert.Equal("203.0.113.10", c.GetValue("ip"));
            Assert.Equal("2181", c.GetValue("port"));
        });
        Assert.Equal(12, doc.GetChanges().Count);
        Assert.Equal(["203.0.113.10:2181"], ServerEndpoints.DistinctEndpoints(doc.Root));

        Assert.True(doc.History.Undo());
        Assert.Empty(doc.GetChanges());
        Assert.False(doc.History.CanUndo);
        Assert.Equal(Fixtures.OriginalIps.Select(ip => $"{ip}:{Fixtures.OriginalPort}"), ServerEndpoints.DistinctEndpoints(doc.Root));

        Assert.True(doc.History.Redo());
        Assert.Equal(12, doc.GetChanges().Count);
    }

    [StockFileFact]
    public void SwitchServer_ToCurrentEndpoint_ReturnsNull()
    {
        var doc = OpenStock();
        doc.Execute(ServerEndpoints.CreateSwitchCommand(doc.Root, "10.0.0.1", Fixtures.OriginalPort)!);
        Assert.Null(ServerEndpoints.CreateSwitchCommand(doc.Root, "10.0.0.1", Fixtures.OriginalPort));
    }

    [StockFileFact]
    public void SwitchServer_WithoutPort_LeavesPortsAlone()
    {
        var doc = OpenStock();
        doc.Execute(ServerEndpoints.CreateSwitchCommand(doc.Root, "10.1.1.1")!);
        Assert.All(ServerEndpoints.FindChannels(doc.Root), c => Assert.Equal(Fixtures.OriginalPort, c.GetValue("port")));
        Assert.Equal(6, doc.GetChanges().Count);
    }

    [StockFileFact]
    public void Typing_IsMergedIntoOneUndoStep_UntilBreakMerge()
    {
        var doc = OpenStock();
        var channel = ServerEndpoints.FindChannels(doc.Root)[0];
        var ip = channel.FindAttribute("ip")!;

        foreach (var text in new[] { "1", "12", "127", "127.0.0.1" })
        {
            doc.Execute(new SetAttributeValueCommand(channel, ip, text));
        }

        doc.History.BreakMerge();
        doc.Execute(new SetAttributeValueCommand(channel, ip, "127.0.0.2"));

        Assert.Equal("127.0.0.2", ip.Value);
        doc.History.Undo();
        Assert.Equal("127.0.0.1", ip.Value);
        doc.History.Undo();
        Assert.Equal(Fixtures.OriginalIp, ip.Value);
        Assert.False(doc.History.CanUndo);
    }

    [StockFileFact]
    public void EditingBackToOriginal_IsNotAChange()
    {
        var doc = OpenStock();
        var channel = ServerEndpoints.FindChannels(doc.Root)[0];
        var ip = channel.FindAttribute("ip")!;
        doc.Execute(new SetAttributeValueCommand(channel, ip, "1.1.1.1"));
        Assert.Single(doc.GetChanges());
        doc.Execute(new SetAttributeValueCommand(channel, ip, Fixtures.OriginalIp));
        Assert.Empty(doc.GetChanges());
    }

    [StockFileFact]
    public void Changes_ReportValueEdit_AndRevertWorks()
    {
        var doc = OpenStock();
        var channel = ServerEndpoints.FindChannels(doc.Root)[2];
        doc.Execute(new SetAttributeValueCommand(channel, channel.FindAttribute("ip")!, "9.9.9.9"));

        var change = Assert.Single(doc.GetChanges());
        Assert.Equal(ChangeKind.AttributeValueChanged, change.Kind);
        Assert.Equal("ip", change.Key);
        Assert.Equal(Fixtures.OriginalIps[2], change.OldValue);
        Assert.Equal("9.9.9.9", change.NewValue);
        Assert.Equal("content › server › channel[3]", change.Path);

        doc.Execute(change.CreateRevertCommand()!);
        Assert.Empty(doc.GetChanges());
        Assert.Equal(Fixtures.OriginalIps[2], channel.GetValue("ip"));
    }

    [StockFileFact]
    public void Changes_AddAndRemoveAttribute_AndRevert()
    {
        var doc = OpenStock();
        var server = doc.Root.Children[0];
        var nameAttribute = server.FindAttribute("name")!;

        doc.Execute(new InsertAttributeCommand(server, new ConfigAttribute("region", "vn")));
        doc.Execute(new RemoveAttributeCommand(server, nameAttribute));

        var changes = doc.GetChanges();
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Kind == ChangeKind.AttributeAdded && c.Key == "region");
        var removed = Assert.Single(changes, c => c.Kind == ChangeKind.AttributeRemoved);
        Assert.Equal("Vietnam-Service", removed.OldValue);

        doc.Execute(removed.CreateRevertCommand()!);
        Assert.Equal("Vietnam-Service", server.GetValue("name"));
        Assert.Equal(0, server.IndexOfAttribute(server.FindAttribute("name")!));
        Assert.Equal(ChangeKind.AttributeAdded, Assert.Single(doc.GetChanges()).Kind);
    }

    [StockFileFact]
    public void Changes_DeleteAndDuplicateElement_AndRevert()
    {
        var doc = OpenStock();
        var server = doc.Root.Children[0];
        var ch2 = server.Children[1];

        doc.Execute(new RemoveNodeCommand(ch2));
        var copy = server.Children[0].DeepClone();
        doc.Execute(new InsertNodeCommand(server, copy, 1));

        var changes = doc.GetChanges();
        Assert.Equal(2, changes.Count);
        var removed = Assert.Single(changes, c => c.Kind == ChangeKind.ElementRemoved);
        Assert.Equal("content › server › channel[2]", removed.Path);
        Assert.Single(changes, c => c.Kind == ChangeKind.ElementAdded);

        doc.Execute(removed.CreateRevertCommand()!);
        Assert.Equal(7, server.Children.Count);
        Assert.Equal("ch-2", server.Children[1].GetValue("name"));

        var added = Assert.Single(doc.GetChanges());
        doc.Execute(added.CreateRevertCommand()!);
        Assert.Empty(doc.GetChanges());
        Assert.True(server.StructurallyEquals(OmFile.Decode(Fixtures.TaanServer).Tree.Root.Children[0]));
    }

    [StockFileFact]
    public void DiscardAllChanges_RestoresOriginal_AsOneUndoStep()
    {
        var doc = OpenStock();
        var original = OmFile.Decode(Fixtures.TaanServer).Tree.Root;
        var server = doc.Root.Children[0];

        doc.Execute(ServerEndpoints.CreateSwitchCommand(doc.Root, "10.0.0.1", "9999")!);
        doc.Execute(new RemoveNodeCommand(server.Children[3]));
        var added = server.Children[0].DeepClone();
        doc.Execute(new InsertNodeCommand(server, added));
        doc.Execute(new SetAttributeValueCommand(added, added.FindAttribute("ip")!, "1.1.1.1"));
        doc.Execute(new InsertAttributeCommand(doc.Root, new ConfigAttribute("x", "y")));

        Assert.True(doc.DiscardAllChanges());
        Assert.Empty(doc.GetChanges());
        Assert.True(doc.Root.StructurallyEquals(original));

        doc.History.Undo(); // brings every change back
        Assert.NotEmpty(doc.GetChanges());
        doc.History.Redo();
        Assert.Empty(doc.GetChanges());
        Assert.False(doc.DiscardAllChanges());
    }

    [StockFileFact]
    public void Change_ExposesAttribute_ForValueEdits()
    {
        var doc = OpenStock();
        var channel = ServerEndpoints.FindChannels(doc.Root)[0];
        var ip = channel.FindAttribute("ip")!;
        doc.Execute(new SetAttributeValueCommand(channel, ip, "1.1.1.1"));
        Assert.Same(ip, Assert.Single(doc.GetChanges()).Attribute);
    }

    [Fact]
    public void ReplaceAll_WholeValue_DoesNotTouchPartialMatches()
    {
        var root = Fixtures.SampleTree();
        root.Children[0].Children[0].FindAttribute("ip")!.Value = "10.0.0.10";
        var command = TreeSearch.CreateReplaceAllCommand(root, "10.0.0.1", "192.168.1.5");
        Assert.NotNull(command);
        Assert.Equal(2, command.Count);
        command.Do();
        Assert.Equal(["10.0.0.10", "192.168.1.5", "192.168.1.5"], root.Children[0].Children.Select(c => c.GetValue("ip")));
    }

    [Fact]
    public void Find_SearchesNamesKeysAndValues()
    {
        var root = Fixtures.SampleTree();
        Assert.Equal(3, TreeSearch.Find(root, "ch-").Count);
        Assert.Equal(3, TreeSearch.Find(root, "channel").Count(m => m.Field == SearchMatchField.ElementName));
        Assert.Equal(3, TreeSearch.Find(root, "PORT").Count);
        Assert.Empty(TreeSearch.Find(root, "PORT", matchCase: true));
    }

    [Fact]
    public void Path_IndexesOnlyRepeatedSiblings()
    {
        var root = Fixtures.SampleTree();
        Assert.Equal("content › server › channel[2]", root.Children[0].Children[1].Path);
        Assert.Equal("content › EnableRanking", root.Children[1].Path);
    }

    [Fact]
    public void InsertChild_RejectsCycles()
    {
        var root = Fixtures.SampleTree();
        var server = root.Children[0];
        root.RemoveChild(server);
        Assert.Throws<InvalidOperationException>(() => server.Children[0].InsertChild(0, server));
    }
}
