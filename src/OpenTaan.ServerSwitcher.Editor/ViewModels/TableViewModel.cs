using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Model;
using OpenTaan.ServerSwitcher.Core.Validation;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

/// <summary>A column of the table. Its width can grow while typing without rebuilding the rows.</summary>
public sealed partial class TableColumn(string key) : ObservableObject
{
    public string Key { get; } = key;

    [ObservableProperty]
    public partial double Width { get; set; }
}

/// <summary>An editable cell. Typing into a cell whose row lacks the attribute adds it.</summary>
public sealed partial class TableCellViewModel : ObservableObject
{
    private readonly IEditorHost _host;
    private string _value = string.Empty;

    public TableCellViewModel(IEditorHost host, ConfigNode node, TableColumn column)
    {
        _host = host;
        Node = node;
        Column = column;
    }

    public ConfigNode Node { get; }

    public TableColumn Column { get; }

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
            var attribute = Node.FindAttribute(Column.Key);
            if (attribute is null)
            {
                _host.Execute(new InsertAttributeCommand(Node, new ConfigAttribute(Column.Key, value)));
            }
            else if (!string.Equals(attribute.Value, value, StringComparison.Ordinal))
            {
                _host.Execute(new SetAttributeValueCommand(Node, attribute, value));
            }
        }
    }

    [ObservableProperty]
    public partial bool IsMissing { get; set; }

    [ObservableProperty]
    public partial bool IsChanged { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string? ToolTip { get; set; }

    internal void Update(DocumentState state)
    {
        var attribute = Node.FindAttribute(Column.Key);
        var value = attribute?.Value ?? string.Empty;
        if (!string.Equals(_value, value, StringComparison.Ordinal))
        {
            _value = value;
            OnPropertyChanged(nameof(Value));
        }

        IsMissing = attribute is null;
        ConfigChange? change = null;
        ValidationProblem? problem = null;
        if (attribute is not null)
        {
            state.AttributeChanges.TryGetValue(attribute, out change);
            state.AttributeProblems.TryGetValue(attribute, out problem);
        }

        IsChanged = change is not null;
        HasError = problem?.Severity == ProblemSeverity.Error;
        ToolTip = problem?.Message
                  ?? (change?.Kind == ChangeKind.AttributeValueChanged ? $"was \"{change.OldValue}\"" : null)
                  ?? (attribute is null ? $"No \"{Column.Key}\" attribute — type to add one" : null);
    }
}

public sealed partial class TableRowViewModel : ObservableObject
{
    private readonly IEditorHost _host;

    public TableRowViewModel(IEditorHost host, ConfigNode node)
    {
        _host = host;
        Node = node;
    }

    public ConfigNode Node { get; }

    public ObservableCollection<TableCellViewModel> Cells { get; } = new();

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsChanged { get; set; }

    [RelayCommand]
    private void Select() => _host.Select(Node);
}

/// <summary>
/// Table tab: the children of one element as rows, one column per attribute key.
/// For TAAN this shows the six channels with name / ip / port side by side.
/// </summary>
public sealed partial class TableViewModel : ObservableObject
{
    private const double LabelWidth = 120;
    private readonly IEditorHost _host;
    private readonly Dictionary<ConfigNode, TableRowViewModel> _rows = new();
    private ConfigNode? _parent;

    public TableViewModel(IEditorHost host)
    {
        _host = host;
    }

    public ObservableCollection<TableColumn> Columns { get; } = new();

    public ObservableCollection<TableRowViewModel> Rows { get; } = new();

    public double LabelColumnWidth => LabelWidth;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRows { get; set; }

    internal void Update(ConfigNode? selected, DocumentState state)
    {
        // Show the selection's children; for a leaf (e.g. one channel), show it among its siblings.
        var parent = selected is null ? null : selected.Children.Count > 0 ? selected : selected.Parent ?? selected;
        if (!ReferenceEquals(parent, _parent))
        {
            _parent = parent;
            _rows.Clear();
            Rows.Clear();
            Columns.Clear();
        }

        if (parent is null || parent.Children.Count == 0)
        {
            HasRows = false;
            Title = "Select an element that has children";
            Rows.Clear();
            Columns.Clear();
            _rows.Clear();
            return;
        }

        HasRows = true;
        Title = $"Children of <{parent.Name}> · {parent.Children.Count} rows";

        // Columns: attribute keys in order of first appearance, sized to their content.
        var keys = parent.Children.SelectMany(c => c.Attributes.Select(a => a.Key)).Distinct(StringComparer.Ordinal).ToList();
        if (!keys.SequenceEqual(Columns.Select(c => c.Key)))
        {
            Columns.Clear();
            foreach (var key in keys)
            {
                Columns.Add(new TableColumn(key));
            }

            _rows.Clear();
            Rows.Clear();
        }

        foreach (var column in Columns)
        {
            column.Width = MeasureColumn(parent, column.Key);
        }

        var desired = new List<TableRowViewModel>();
        foreach (var child in parent.Children)
        {
            if (!_rows.TryGetValue(child, out var row))
            {
                row = new TableRowViewModel(_host, child);
                foreach (var column in Columns)
                {
                    row.Cells.Add(new TableCellViewModel(_host, child, column));
                }

                _rows[child] = row;
            }

            row.Label = child.Segment;
            row.IsSelected = ReferenceEquals(child, selected);
            row.IsChanged = state.ChangedNodes.Contains(child);
            foreach (var cell in row.Cells)
            {
                cell.Update(state);
            }

            desired.Add(row);
        }

        foreach (var stale in _rows.Keys.Except(parent.Children).ToList())
        {
            _rows.Remove(stale);
        }

        Rows.SyncTo(desired);
    }

    private static double MeasureColumn(ConfigNode parent, string key)
    {
        // ~7.6 px per monospace character at 12.5 px, plus padding; rounded to steps of 20 px.
        var longest = parent.Children.Select(c => c.GetValue(key)?.Length ?? 0).Append(key.Length).Max();
        var width = Math.Ceiling((longest * 7.6 + 32) / 20) * 20;
        return Math.Clamp(width, 90, 360);
    }
}
