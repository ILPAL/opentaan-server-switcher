using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Validation;

namespace OpenTaan.ServerSwitcher.Editor.ViewModels;

/// <summary>A card in the Changes tab: "− ip="old"" / "+ ip="new"" with Revert.</summary>
public sealed partial class ChangeItemViewModel : ObservableObject
{
    private readonly IEditorHost _host;
    private readonly ConfigChange _change;

    public ChangeItemViewModel(IEditorHost host, ConfigChange change)
    {
        _host = host;
        _change = change;
        Path = change.Path;
        (RemovedLine, AddedLine) = Describe(change);
        Kind = change.Kind switch
        {
            ChangeKind.AttributeValueChanged => "Value changed",
            ChangeKind.AttributeRenamed => "Attribute renamed",
            ChangeKind.AttributeAdded => "Attribute added",
            ChangeKind.AttributeRemoved => "Attribute removed",
            ChangeKind.ElementRenamed => "Element renamed",
            ChangeKind.MetadataChanged => "Metadata changed",
            ChangeKind.ElementAdded => "Element added",
            ChangeKind.ElementRemoved => "Element removed",
            _ => change.Kind.ToString(),
        };
    }

    public string Path { get; }

    public string Kind { get; }

    public string? RemovedLine { get; }

    public string? AddedLine { get; }

    public bool HasRemovedLine => RemovedLine is not null;

    public bool HasAddedLine => AddedLine is not null;

    [RelayCommand]
    private void Revert()
    {
        if (_change.CreateRevertCommand() is { } command)
        {
            _host.Execute(command);
        }
    }

    [RelayCommand]
    private void Go()
    {
        if (_change.Node.Root == _host.Document?.Root)
        {
            _host.Select(_change.Node);
        }
    }

    private static (string? Removed, string? Added) Describe(ConfigChange c) => c.Kind switch
    {
        ChangeKind.AttributeValueChanged => ($"{c.Key}=\"{c.OldValue}\"", $"{c.Key}=\"{c.NewValue}\""),
        ChangeKind.AttributeRenamed => ($"{c.OldValue}=…", $"{c.NewValue}=…"),
        ChangeKind.AttributeAdded => (null, $"{c.Key}=\"{c.NewValue}\""),
        ChangeKind.AttributeRemoved => ($"{c.Key}=\"{c.OldValue}\"", null),
        ChangeKind.ElementRenamed => ($"<{c.OldValue}>", $"<{c.NewValue}>"),
        ChangeKind.MetadataChanged => ($"metadata \"{c.OldValue}\"", $"metadata \"{c.NewValue}\""),
        ChangeKind.ElementAdded => (null, $"<{c.Key}{Attrs(c)}>"),
        ChangeKind.ElementRemoved => ($"<{c.Key}>", null),
        _ => (c.OldValue, c.NewValue),
    };

    private static string Attrs(ConfigChange c) =>
        c.Kind == ChangeKind.ElementAdded
            ? string.Concat(c.Node.Attributes.Take(3).Select(a => $" {a.Key}=\"{a.Value}\""))
            : string.Empty;
}

/// <summary>A row of the Problems panel.</summary>
public sealed partial class ProblemItemViewModel(IEditorHost host, ValidationProblem problem) : ObservableObject
{
    public string Message => problem.Message;

    public string Location => problem.Location;

    public bool IsError => problem.Severity == ProblemSeverity.Error;

    public bool IsWarning => !IsError;

    [RelayCommand]
    private void Go() => host.Select(problem.Node);
}

/// <summary>A .0m file in the folder of the open document (left sidebar).</summary>
public sealed partial class FileEntryViewModel(string path, Func<string, Task> open) : ObservableObject
{
    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsModified { get; set; }

    [RelayCommand]
    private Task Open() => open(Path);
}
