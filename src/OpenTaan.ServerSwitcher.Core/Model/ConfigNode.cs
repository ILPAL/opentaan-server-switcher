namespace OpenTaan.ServerSwitcher.Core.Model;

/// <summary>
/// One node of a .0m configuration tree: a name, a metadata string, an ordered list of
/// key/value attributes (keys may repeat) and an ordered list of child nodes.
/// </summary>
/// <remarks>
/// Every node and attribute has a process-unique <see cref="Id"/> that survives edits. Change tracking
/// compares the current tree with a snapshot by id, so an edited attribute is reported as "changed"
/// rather than "removed + added".
/// </remarks>
public sealed class ConfigNode
{
    private static int s_nextId;

    private readonly List<ConfigAttribute> _attributes = new();
    private readonly List<ConfigNode> _children = new();

    public ConfigNode(string name, string metadata = "")
        : this(NewId(), name, metadata)
    {
    }

    internal ConfigNode(int id, string name, string metadata)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    internal static int NewId() => Interlocked.Increment(ref s_nextId);

    public int Id { get; }

    public string Name { get; set; }

    public string Metadata { get; set; }

    public ConfigNode? Parent { get; private set; }

    public IReadOnlyList<ConfigAttribute> Attributes => _attributes;

    public IReadOnlyList<ConfigNode> Children => _children;

    public int IndexInParent => Parent?._children.IndexOf(this) ?? -1;

    public bool IsAncestorOf(ConfigNode node)
    {
        for (var p = node.Parent; p is not null; p = p.Parent)
        {
            if (ReferenceEquals(p, this))
            {
                return true;
            }
        }

        return false;
    }

    // ---- Attributes -------------------------------------------------------------------------

    public ConfigAttribute AddAttribute(string key, string value)
    {
        var attribute = new ConfigAttribute(key, value);
        _attributes.Add(attribute);
        return attribute;
    }

    public void InsertAttribute(int index, ConfigAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        if (_attributes.Contains(attribute))
        {
            throw new InvalidOperationException("Attribute is already part of this node.");
        }

        _attributes.Insert(Math.Clamp(index, 0, _attributes.Count), attribute);
    }

    /// <summary>Removes the attribute and returns the index it had, or -1 when it was not present.</summary>
    public int RemoveAttribute(ConfigAttribute attribute)
    {
        var index = _attributes.IndexOf(attribute);
        if (index >= 0)
        {
            _attributes.RemoveAt(index);
        }

        return index;
    }

    public int IndexOfAttribute(ConfigAttribute attribute) => _attributes.IndexOf(attribute);

    /// <summary>First attribute with the given key (ordinal comparison), or null.</summary>
    public ConfigAttribute? FindAttribute(string key) =>
        _attributes.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal));

    public string? GetValue(string key) => FindAttribute(key)?.Value;

    // ---- Children ---------------------------------------------------------------------------

    public ConfigNode AddChild(ConfigNode child)
    {
        InsertChild(_children.Count, child);
        return child;
    }

    public void InsertChild(int index, ConfigNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new InvalidOperationException("Node already has a parent; remove it first.");
        }

        if (ReferenceEquals(child, this) || child.IsAncestorOf(this))
        {
            throw new InvalidOperationException("A node cannot contain itself.");
        }

        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
        child.Parent = this;
    }

    /// <summary>Removes the child and returns the index it had, or -1 when it was not a child.</summary>
    public int RemoveChild(ConfigNode child)
    {
        var index = _children.IndexOf(child);
        if (index >= 0)
        {
            _children.RemoveAt(index);
            child.Parent = null;
        }

        return index;
    }

    // ---- Traversal --------------------------------------------------------------------------

    /// <summary>This node followed by all descendants, depth-first in document order.</summary>
    public IEnumerable<ConfigNode> DescendantsAndSelf()
    {
        var stack = new Stack<ConfigNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node._children.Count - 1; i >= 0; i--)
            {
                stack.Push(node._children[i]);
            }
        }
    }

    public IEnumerable<ConfigNode> Descendants() => DescendantsAndSelf().Skip(1);

    public IEnumerable<ConfigNode> Ancestors()
    {
        for (var p = Parent; p is not null; p = p.Parent)
        {
            yield return p;
        }
    }

    public ConfigNode Root => Ancestors().LastOrDefault() ?? this;

    /// <summary>
    /// Human-readable location, e.g. <c>content › server › channel[3]</c>. A position index (1-based) is
    /// added only when the parent has several children with the same name.
    /// </summary>
    public string Path => string.Join(" › ", Ancestors().Reverse().Append(this).Select(n => n.Segment));

    /// <summary>The last segment of <see cref="Path"/>.</summary>
    public string Segment
    {
        get
        {
            var label = Name.Length == 0 ? "(unnamed)" : Name;
            if (Parent is null)
            {
                return label;
            }

            var sameName = 0;
            var position = 0;
            foreach (var sibling in Parent._children)
            {
                if (string.Equals(sibling.Name, Name, StringComparison.Ordinal))
                {
                    sameName++;
                    if (ReferenceEquals(sibling, this))
                    {
                        position = sameName;
                    }
                }
            }

            return sameName > 1 ? $"{label}[{position}]" : label;
        }
    }

    // ---- Copying ----------------------------------------------------------------------------

    /// <summary>Deep copy with fresh ids (used for "Duplicate").</summary>
    public ConfigNode DeepClone()
    {
        var copy = new ConfigNode(Name, Metadata);
        foreach (var a in _attributes)
        {
            copy._attributes.Add(new ConfigAttribute(a.Key, a.Value));
        }

        foreach (var c in _children)
        {
            copy.AddChild(c.DeepClone());
        }

        return copy;
    }

    /// <summary>Structural equality: names, metadata, attributes (in order) and children (in order). Ids are ignored.</summary>
    public bool StructurallyEquals(ConfigNode other)
    {
        if (!string.Equals(Name, other.Name, StringComparison.Ordinal) ||
            !string.Equals(Metadata, other.Metadata, StringComparison.Ordinal) ||
            _attributes.Count != other._attributes.Count ||
            _children.Count != other._children.Count)
        {
            return false;
        }

        for (var i = 0; i < _attributes.Count; i++)
        {
            if (!string.Equals(_attributes[i].Key, other._attributes[i].Key, StringComparison.Ordinal) ||
                !string.Equals(_attributes[i].Value, other._attributes[i].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var i = 0; i < _children.Count; i++)
        {
            if (!_children[i].StructurallyEquals(other._children[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => $"<{Name}> ({_attributes.Count} attributes, {_children.Count} children)";
}
