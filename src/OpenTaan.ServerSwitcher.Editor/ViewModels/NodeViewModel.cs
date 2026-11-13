using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

/// <summary>A <c>key="value"</c> chip shown inline on a tree row.</summary>
public sealed record AttributeChip(string Key, string Value, bool IsChanged);

/// <summary>One row of the Tree view. Reused across refreshes so expansion and selection survive edits.</summary>
public sealed partial class NodeViewModel : ObservableObject
{
    private const int MaxChips = 4;

    public NodeViewModel(ConfigNode node)
    {
        Node = node;
    }

    public ConfigNode Node { get; }

    public ObservableCollection<NodeViewModel> Children { get; } = new();

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<AttributeChip> Chips { get; set; } = [];

    [ObservableProperty]
    public partial string MoreLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChildCountLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsChanged { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsMatch { get; set; }

    /// <summary>Updates this row from the model (not its children).</summary>
    internal void Update(DocumentState state)
    {
        DisplayName = Node.Name.Length == 0 ? "(unnamed)" : Node.Name;

        var chips = Node.Attributes
            .Take(MaxChips)
            .Select(a => new AttributeChip(a.Key, a.Value, state.AttributeChanges.ContainsKey(a)))
            .ToList();
        if (!chips.SequenceEqual(Chips))
        {
            Chips = chips;
        }

        MoreLabel = Node.Attributes.Count > MaxChips ? $"+{Node.Attributes.Count - MaxChips}" : string.Empty;
        ChildCountLabel = Node.Children.Count switch
        {
            0 => string.Empty,
            1 => "1 child",
            var n => $"{n} children",
        };
        IsChanged = state.ChangedNodes.Contains(Node);
        HasError = state.Problems.Any(p => p.Node == Node && p.Severity == Core.Validation.ProblemSeverity.Error);
        IsMatch = state.Matches.Contains(Node);
    }
}
