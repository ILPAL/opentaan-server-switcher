using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Editing;

/// <summary>A reversible change to a config tree.</summary>
public interface IEditCommand
{
    string Description { get; }

    void Do();

    void Undo();
}

/// <summary>A command that can absorb the next command, so typing "1", "12", "123" becomes one undo step.</summary>
public interface IMergeableEditCommand : IEditCommand
{
    /// <summary>Try to fold <paramref name="next"/> (already executed) into this command.</summary>
    bool TryMerge(IEditCommand next);
}

public sealed class SetAttributeValueCommand : IMergeableEditCommand
{
    private readonly string _oldValue;
    private string _newValue;

    public SetAttributeValueCommand(ConfigNode node, ConfigAttribute attribute, string newValue)
    {
        Node = node;
        Attribute = attribute;
        _oldValue = attribute.Value;
        _newValue = newValue;
    }

    public ConfigNode Node { get; }

    public ConfigAttribute Attribute { get; }

    public string Description => $"Set {Attribute.Key}";

    public void Do() => Attribute.Value = _newValue;

    public void Undo() => Attribute.Value = _oldValue;

    public bool TryMerge(IEditCommand next)
    {
        if (next is SetAttributeValueCommand other && ReferenceEquals(other.Attribute, Attribute))
        {
            _newValue = other._newValue;
            return true;
        }

        return false;
    }
}

public sealed class SetAttributeKeyCommand : IMergeableEditCommand
{
    private readonly string _oldKey;
    private string _newKey;

    public SetAttributeKeyCommand(ConfigNode node, ConfigAttribute attribute, string newKey)
    {
        Node = node;
        Attribute = attribute;
        _oldKey = attribute.Key;
        _newKey = newKey;
    }

    public ConfigNode Node { get; }

    public ConfigAttribute Attribute { get; }

    public string Description => $"Rename attribute {_oldKey}";

    public void Do() => Attribute.Key = _newKey;

    public void Undo() => Attribute.Key = _oldKey;

    public bool TryMerge(IEditCommand next)
    {
        if (next is SetAttributeKeyCommand other && ReferenceEquals(other.Attribute, Attribute))
        {
            _newKey = other._newKey;
            return true;
        }

        return false;
    }
}

public sealed class InsertAttributeCommand : IEditCommand
{
    private readonly int _index;

    public InsertAttributeCommand(ConfigNode node, ConfigAttribute attribute, int index = int.MaxValue)
    {
        Node = node;
        Attribute = attribute;
        _index = index;
    }

    public ConfigNode Node { get; }

    public ConfigAttribute Attribute { get; }

    public string Description => $"Add attribute {Attribute.Key}";

    public void Do() => Node.InsertAttribute(_index, Attribute);

    public void Undo() => Node.RemoveAttribute(Attribute);
}

public sealed class RemoveAttributeCommand : IEditCommand
{
    private int _index = -1;

    public RemoveAttributeCommand(ConfigNode node, ConfigAttribute attribute)
    {
        Node = node;
        Attribute = attribute;
    }

    public ConfigNode Node { get; }

    public ConfigAttribute Attribute { get; }

    public string Description => $"Remove attribute {Attribute.Key}";

    public void Do() => _index = Node.RemoveAttribute(Attribute);

    public void Undo()
    {
        if (_index >= 0)
        {
            Node.InsertAttribute(_index, Attribute);
        }
    }
}

public sealed class SetNodeNameCommand : IMergeableEditCommand
{
    private readonly string _oldName;
    private string _newName;

    public SetNodeNameCommand(ConfigNode node, string newName)
    {
        Node = node;
        _oldName = node.Name;
        _newName = newName;
    }

    public ConfigNode Node { get; }

    public string Description => "Rename element";

    public void Do() => Node.Name = _newName;

    public void Undo() => Node.Name = _oldName;

    public bool TryMerge(IEditCommand next)
    {
        if (next is SetNodeNameCommand other && ReferenceEquals(other.Node, Node))
        {
            _newName = other._newName;
            return true;
        }

        return false;
    }
}

public sealed class SetNodeMetadataCommand : IMergeableEditCommand
{
    private readonly string _oldValue;
    private string _newValue;

    public SetNodeMetadataCommand(ConfigNode node, string newValue)
    {
        Node = node;
        _oldValue = node.Metadata;
        _newValue = newValue;
    }

    public ConfigNode Node { get; }

    public string Description => "Edit metadata";

    public void Do() => Node.Metadata = _newValue;

    public void Undo() => Node.Metadata = _oldValue;

    public bool TryMerge(IEditCommand next)
    {
        if (next is SetNodeMetadataCommand other && ReferenceEquals(other.Node, Node))
        {
            _newValue = other._newValue;
            return true;
        }

        return false;
    }
}

public sealed class InsertNodeCommand : IEditCommand
{
    private readonly int _index;

    public InsertNodeCommand(ConfigNode parent, ConfigNode node, int index = int.MaxValue, string? description = null)
    {
        Parent = parent;
        Node = node;
        _index = index;
        Description = description ?? $"Add <{node.Name}>";
    }

    public ConfigNode Parent { get; }

    public ConfigNode Node { get; }

    public string Description { get; }

    public void Do() => Parent.InsertChild(_index, Node);

    public void Undo() => Parent.RemoveChild(Node);
}

public sealed class RemoveNodeCommand : IEditCommand
{
    private int _index = -1;

    public RemoveNodeCommand(ConfigNode node)
    {
        Parent = node.Parent ?? throw new ArgumentException("The root element cannot be removed.", nameof(node));
        Node = node;
    }

    public ConfigNode Parent { get; }

    public ConfigNode Node { get; }

    public string Description => $"Delete <{Node.Name}>";

    public void Do() => _index = Parent.RemoveChild(Node);

    public void Undo()
    {
        if (_index >= 0)
        {
            Parent.InsertChild(_index, Node);
        }
    }
}

/// <summary>Several commands applied and undone as one step (e.g. "Switch server", "Replace all").</summary>
public sealed class CompositeCommand : IEditCommand
{
    private readonly IReadOnlyList<IEditCommand> _commands;

    public CompositeCommand(string description, IEnumerable<IEditCommand> commands)
    {
        Description = description;
        _commands = commands.ToList();
    }

    public string Description { get; }

    public int Count => _commands.Count;

    public IReadOnlyList<IEditCommand> Commands => _commands;

    public void Do()
    {
        foreach (var c in _commands)
        {
            c.Do();
        }
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}
