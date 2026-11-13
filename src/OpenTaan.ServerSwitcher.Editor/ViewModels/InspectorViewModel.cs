using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Model;
using OpenTaan.ServerSwitcher.Core.Validation;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

/// <summary>One editable attribute in the inspector ("ip", "port", …).</summary>
public sealed partial class AttributeFieldViewModel : ObservableObject
{
    private readonly IEditorHost _host;
    private string _value;

    public AttributeFieldViewModel(IEditorHost host, ConfigNode node, ConfigAttribute attribute)
    {
        _host = host;
        Node = node;
        Attribute = attribute;
        _value = attribute.Value;
    }

    public ConfigNode Node { get; }

    public ConfigAttribute Attribute { get; }

    [ObservableProperty]
    public partial string Key { get; set; } = string.Empty;

    /// <summary>Two-way bound to the text box; every change is an undoable edit (consecutive keystrokes merge).</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (string.Equals(_value, value, StringComparison.Ordinal))
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
            if (!string.Equals(Attribute.Value, value, StringComparison.Ordinal))
            {
                _host.Execute(new SetAttributeValueCommand(Node, Attribute, value));
            }
        }
    }

    [ObservableProperty]
    public partial bool IsChanged { get; set; }

    [ObservableProperty]
    public partial string OriginalValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? Warning { get; set; }

    public bool HasError => Error is not null;

    public bool HasWarning => Warning is not null;

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnWarningChanged(string? value) => OnPropertyChanged(nameof(HasWarning));

    internal void Update(DocumentState state)
    {
        Key = Attribute.Key;
        if (!string.Equals(_value, Attribute.Value, StringComparison.Ordinal))
        {
            _value = Attribute.Value;
            OnPropertyChanged(nameof(Value));
        }

        if (state.AttributeChanges.TryGetValue(Attribute, out var change) && change.Kind == ChangeKind.AttributeValueChanged)
        {
            IsChanged = true;
            OriginalValue = change.OldValue ?? string.Empty;
        }
        else
        {
            IsChanged = state.AttributeChanges.ContainsKey(Attribute);
            OriginalValue = string.Empty;
        }

        state.AttributeProblems.TryGetValue(Attribute, out var problem);
        Error = problem?.Severity == ProblemSeverity.Error ? problem.Message : null;
        Warning = problem?.Severity == ProblemSeverity.Warning ? problem.Message : null;
    }

    [RelayCommand]
    private void Revert()
    {
        if (_host.State.AttributeChanges.TryGetValue(Attribute, out var change) && change.CreateRevertCommand() is { } command)
        {
            _host.Execute(command);
        }
    }

    [RelayCommand]
    private void Remove() => _host.Execute(new RemoveAttributeCommand(Node, Attribute));
}

/// <summary>A child shown in the inspector's "Children" list.</summary>
public sealed record ChildSummary(ConfigNode Node, string Name, string Summary);

/// <summary>Right-hand panel: the selected element, its attributes and children.</summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly IEditorHost _host;
    private readonly Dictionary<ConfigAttribute, AttributeFieldViewModel> _fields = new();
    private ConfigNode? _node;
    private string _elementName = string.Empty;
    private string _metadata = string.Empty;

    public InspectorViewModel(IEditorHost host)
    {
        _host = host;
    }

    public ConfigNode? Node => _node;

    public bool HasSelection => _node is not null;

    public bool IsRoot => _node?.Parent is null;

    public bool CanDelete => _node?.Parent is not null;

    public ObservableCollection<AttributeFieldViewModel> Fields { get; } = new();

    [ObservableProperty]
    public partial IReadOnlyList<ChildSummary> Children { get; set; } = [];

    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChildrenHeader { get; set; } = "Children";

    [ObservableProperty]
    public partial bool IsAddingAttribute { get; set; }

    [ObservableProperty]
    public partial string NewKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AddError { get; set; }

    public string ElementName
    {
        get => _elementName;
        set
        {
            if (_node is null || string.Equals(_elementName, value, StringComparison.Ordinal))
            {
                return;
            }

            _elementName = value;
            OnPropertyChanged();
            _host.Execute(new SetNodeNameCommand(_node, value));
        }
    }

    public string Metadata
    {
        get => _metadata;
        set
        {
            if (_node is null || string.Equals(_metadata, value, StringComparison.Ordinal))
            {
                return;
            }

            _metadata = value;
            OnPropertyChanged();
            _host.Execute(new SetNodeMetadataCommand(_node, value));
        }
    }

    internal void Show(ConfigNode? node, DocumentState state)
    {
        if (!ReferenceEquals(node, _node))
        {
            _node = node;
            _fields.Clear();
            Fields.Clear();
            IsAddingAttribute = false;
            OnPropertyChanged(nameof(Node));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsRoot));
            OnPropertyChanged(nameof(CanDelete));
            DuplicateCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }

        if (_node is null)
        {
            return;
        }

        if (!string.Equals(_elementName, _node.Name, StringComparison.Ordinal))
        {
            _elementName = _node.Name;
            OnPropertyChanged(nameof(ElementName));
        }

        if (!string.Equals(_metadata, _node.Metadata, StringComparison.Ordinal))
        {
            _metadata = _node.Metadata;
            OnPropertyChanged(nameof(Metadata));
        }

        Path = _node.Path;

        var desired = new List<AttributeFieldViewModel>();
        foreach (var attribute in _node.Attributes)
        {
            if (!_fields.TryGetValue(attribute, out var field))
            {
                field = new AttributeFieldViewModel(_host, _node, attribute);
                _fields[attribute] = field;
            }

            field.Update(state);
            desired.Add(field);
        }

        foreach (var stale in _fields.Keys.Except(_node.Attributes).ToList())
        {
            _fields.Remove(stale);
        }

        Fields.SyncTo(desired);

        Children = _node.Children
            .Select(c => new ChildSummary(c, c.Name, string.Join(" ", c.Attributes.Select(a => $"{a.Key}=\"{a.Value}\""))))
            .ToList();
        ChildrenHeader = $"Children · {_node.Children.Count}";
    }

    [RelayCommand]
    private void SelectChild(ChildSummary? child)
    {
        if (child is not null)
        {
            _host.Select(child.Node);
        }
    }

    [RelayCommand]
    private void BeginAddAttribute()
    {
        NewKey = string.Empty;
        NewValue = string.Empty;
        AddError = null;
        IsAddingAttribute = true;
    }

    [RelayCommand]
    private void CancelAddAttribute() => IsAddingAttribute = false;

    [RelayCommand]
    private void ConfirmAddAttribute()
    {
        if (_node is null)
        {
            return;
        }

        var key = NewKey.Trim();
        if (key.Length == 0)
        {
            AddError = "Enter a key";
            return;
        }

        if (_node.FindAttribute(key) is not null)
        {
            AddError = $"\"{key}\" already exists on this element";
            return;
        }

        IsAddingAttribute = false;
        _host.Execute(new InsertAttributeCommand(_node, new ConfigAttribute(key, NewValue)));
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Duplicate()
    {
        if (_node?.Parent is not { } parent)
        {
            return;
        }

        var copy = _node.DeepClone();
        _host.Execute(new InsertNodeCommand(parent, copy, _node.IndexInParent + 1, $"Duplicate <{_node.Name}>"));
        _host.Select(copy);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (_node?.Parent is not { } parent)
        {
            return;
        }

        var index = _node.IndexInParent;
        _host.Execute(new RemoveNodeCommand(_node));
        _host.Select(parent.Children.Count == 0 ? parent : parent.Children[Math.Min(index, parent.Children.Count - 1)]);
    }

    [RelayCommand]
    private void AddChild()
    {
        if (_node is null)
        {
            return;
        }

        // New child copies the attribute keys of an existing sibling-to-be (e.g. a new channel gets name/ip/port).
        var template = _node.Children.LastOrDefault();
        var child = new ConfigNode(template?.Name ?? "item");
        if (template is not null)
        {
            foreach (var attribute in template.Attributes)
            {
                child.AddAttribute(attribute.Key, string.Empty);
            }
        }

        _host.Execute(new InsertNodeCommand(_node, child));
        _host.Select(child);
    }
}
