using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Editing;

public enum ChangeKind
{
    AttributeValueChanged,
    AttributeRenamed,
    AttributeAdded,
    AttributeRemoved,
    ElementRenamed,
    MetadataChanged,
    ElementAdded,
    ElementRemoved,
}

/// <summary>One difference between the saved tree and the current tree.</summary>
public sealed class ConfigChange
{
    private readonly Func<IEditCommand?> _revert;

    internal ConfigChange(ChangeKind kind, ConfigNode node, string path, string? key, string? oldValue, string? newValue, Func<IEditCommand?> revert, ConfigAttribute? attribute = null)
    {
        Kind = kind;
        Node = node;
        Attribute = attribute;
        Path = path;
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
        _revert = revert;
    }

    public ChangeKind Kind { get; }

    /// <summary>The affected node in the current tree (for a removed element: its former parent).</summary>
    public ConfigNode Node { get; }

    /// <summary>The affected attribute in the current tree (null for element changes and removed attributes).</summary>
    public ConfigAttribute? Attribute { get; }

    /// <summary>Location shown to the user, e.g. <c>content › server › channel[2]</c>.</summary>
    public string Path { get; }

    /// <summary>Attribute key (or element name for element changes).</summary>
    public string? Key { get; }

    public string? OldValue { get; }

    public string? NewValue { get; }

    /// <summary>A command that undoes just this change, or null when it can no longer be reverted on its own.</summary>
    public IEditCommand? CreateRevertCommand() => _revert();

    public override string ToString() => $"{Kind} {Path} {Key}: \"{OldValue}\" -> \"{NewValue}\"";
}

/// <summary>Immutable copy of a tree's content keyed by node/attribute id.</summary>
public sealed class TreeSnapshot
{
    private readonly Dictionary<int, NodeSnap> _nodes;

    private TreeSnapshot(Dictionary<int, NodeSnap> nodes, int rootId)
    {
        _nodes = nodes;
        RootId = rootId;
    }

    internal int RootId { get; }

    internal sealed record AttrSnap(int Id, string Key, string Value);

    internal sealed record NodeSnap(int Id, string Name, string Metadata, IReadOnlyList<AttrSnap> Attributes, IReadOnlyList<int> ChildIds, string Path);

    public static TreeSnapshot Capture(ConfigNode root)
    {
        var nodes = new Dictionary<int, NodeSnap>();
        foreach (var n in root.DescendantsAndSelf())
        {
            nodes[n.Id] = new NodeSnap(
                n.Id,
                n.Name,
                n.Metadata,
                n.Attributes.Select(a => new AttrSnap(a.Id, a.Key, a.Value)).ToList(),
                n.Children.Select(c => c.Id).ToList(),
                n.Path);
        }

        return new TreeSnapshot(nodes, root.Id);
    }

    internal bool TryGet(int id, out NodeSnap snap) => _nodes.TryGetValue(id, out snap!);

    /// <summary>Recreates a node subtree from the snapshot, keeping the original ids.</summary>
    internal ConfigNode Rebuild(int id)
    {
        var snap = _nodes[id];
        var node = new ConfigNode(snap.Id, snap.Name, snap.Metadata);
        foreach (var a in snap.Attributes)
        {
            node.InsertAttribute(int.MaxValue, new ConfigAttribute(a.Id, a.Key, a.Value));
        }

        foreach (var childId in snap.ChildIds)
        {
            node.AddChild(Rebuild(childId));
        }

        return node;
    }
}

/// <summary>Computes the list of <see cref="ConfigChange"/>s between a snapshot and the current tree.</summary>
public static class ChangeTracker
{
    public static IReadOnlyList<ConfigChange> Compare(TreeSnapshot baseline, ConfigNode root)
    {
        var changes = new List<ConfigChange>();
        var currentIds = root.DescendantsAndSelf().Select(n => n.Id).ToHashSet();
        Visit(baseline, root, currentIds, changes);
        return changes;
    }

    private static void Visit(TreeSnapshot baseline, ConfigNode node, HashSet<int> currentIds, List<ConfigChange> changes)
    {
        if (!baseline.TryGet(node.Id, out var snap))
        {
            changes.Add(new ConfigChange(
                ChangeKind.ElementAdded, node, node.Path, node.Name, null, null,
                () => node.Parent is null ? null : new RemoveNodeCommand(node)));
            return; // the whole subtree is new
        }

        if (!string.Equals(node.Name, snap.Name, StringComparison.Ordinal))
        {
            var original = snap.Name;
            changes.Add(new ConfigChange(
                ChangeKind.ElementRenamed, node, node.Path, null, snap.Name, node.Name,
                () => new SetNodeNameCommand(node, original)));
        }

        if (!string.Equals(node.Metadata, snap.Metadata, StringComparison.Ordinal))
        {
            var original = snap.Metadata;
            changes.Add(new ConfigChange(
                ChangeKind.MetadataChanged, node, node.Path, null, snap.Metadata, node.Metadata,
                () => new SetNodeMetadataCommand(node, original)));
        }

        CompareAttributes(node, snap, changes);

        // Elements that were children of this node and no longer exist anywhere.
        for (var i = 0; i < snap.ChildIds.Count; i++)
        {
            var childId = snap.ChildIds[i];
            if (currentIds.Contains(childId) || !baseline.TryGet(childId, out var childSnap))
            {
                continue;
            }

            var position = i;
            changes.Add(new ConfigChange(
                ChangeKind.ElementRemoved, node, childSnap.Path, childSnap.Name, null, null,
                () => new InsertNodeCommand(
                    node,
                    baseline.Rebuild(childId),
                    RestoreIndex(snap.ChildIds, position, node.Children.Select(c => c.Id).ToList()),
                    $"Restore <{childSnap.Name}>")));
        }

        foreach (var child in node.Children)
        {
            Visit(baseline, child, currentIds, changes);
        }
    }

    private static void CompareAttributes(ConfigNode node, TreeSnapshot.NodeSnap snap, List<ConfigChange> changes)
    {
        var originals = snap.Attributes.ToDictionary(a => a.Id);
        var path = node.Path;

        foreach (var attribute in node.Attributes)
        {
            if (!originals.TryGetValue(attribute.Id, out var original))
            {
                changes.Add(new ConfigChange(
                    ChangeKind.AttributeAdded, node, path, attribute.Key, null, attribute.Value,
                    () => new RemoveAttributeCommand(node, attribute), attribute));
                continue;
            }

            if (!string.Equals(attribute.Key, original.Key, StringComparison.Ordinal))
            {
                changes.Add(new ConfigChange(
                    ChangeKind.AttributeRenamed, node, path, attribute.Key, original.Key, attribute.Key,
                    () => new SetAttributeKeyCommand(node, attribute, original.Key), attribute));
            }

            if (!string.Equals(attribute.Value, original.Value, StringComparison.Ordinal))
            {
                changes.Add(new ConfigChange(
                    ChangeKind.AttributeValueChanged, node, path, attribute.Key, original.Value, attribute.Value,
                    () => new SetAttributeValueCommand(node, attribute, original.Value), attribute));
            }
        }

        var currentAttributeIds = node.Attributes.Select(a => a.Id).ToHashSet();
        for (var i = 0; i < snap.Attributes.Count; i++)
        {
            var original = snap.Attributes[i];
            if (currentAttributeIds.Contains(original.Id))
            {
                continue;
            }

            var position = i;
            var originalIds = snap.Attributes.Select(a => a.Id).ToList();
            changes.Add(new ConfigChange(
                ChangeKind.AttributeRemoved, node, path, original.Key, original.Value, null,
                () => new InsertAttributeCommand(
                    node,
                    new ConfigAttribute(original.Id, original.Key, original.Value),
                    RestoreIndex(originalIds, position, node.Attributes.Select(a => a.Id).ToList()))));
        }
    }

    /// <summary>
    /// Where to re-insert the item that was at <paramref name="position"/> in <paramref name="originalIds"/>:
    /// right after the nearest earlier original sibling that is still present, or at the start when there is none.
    /// Computed when the revert runs, so restoring removed siblings in any order recreates the original order.
    /// </summary>
    private static int RestoreIndex(IReadOnlyList<int> originalIds, int position, List<int> currentIds)
    {
        for (var j = position - 1; j >= 0; j--)
        {
            var index = currentIds.IndexOf(originalIds[j]);
            if (index >= 0)
            {
                return index + 1;
            }
        }

        return 0;
    }
}
