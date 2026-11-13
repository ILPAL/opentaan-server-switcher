using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Model;
using OpenTaan.ServerSwitcher.Core.Validation;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

/// <summary>
/// Everything derived from the document after an edit (changes, validation, search hits), indexed for the views.
/// Rebuilt once per edit, then read by the tree, table, inspector and lists.
/// </summary>
public sealed class DocumentState
{
    public static DocumentState Empty { get; } = new([], [], []);

    public DocumentState(IReadOnlyList<ConfigChange> changes, IReadOnlyList<ValidationProblem> problems, IEnumerable<ConfigNode> matches)
    {
        Changes = changes;
        Problems = problems;
        Matches = matches.ToHashSet();

        foreach (var change in changes)
        {
            ChangedNodes.Add(change.Node);
            if (change.Attribute is { } attribute)
            {
                // Value changes win over renames for the "was …" hint.
                if (change.Kind == ChangeKind.AttributeValueChanged || !AttributeChanges.ContainsKey(attribute))
                {
                    AttributeChanges[attribute] = change;
                }
            }
        }

        foreach (var problem in problems)
        {
            if (problem.Attribute is { } attribute)
            {
                if (!AttributeProblems.TryGetValue(attribute, out var existing) || problem.Severity > existing.Severity)
                {
                    AttributeProblems[attribute] = problem;
                }
            }
        }
    }

    public IReadOnlyList<ConfigChange> Changes { get; }

    public IReadOnlyList<ValidationProblem> Problems { get; }

    public HashSet<ConfigNode> Matches { get; }

    public HashSet<ConfigNode> ChangedNodes { get; } = new();

    public Dictionary<ConfigAttribute, ConfigChange> AttributeChanges { get; } = new();

    public Dictionary<ConfigAttribute, ValidationProblem> AttributeProblems { get; } = new();

    public int ErrorCount => Problems.Count(p => p.Severity == ProblemSeverity.Error);
}
