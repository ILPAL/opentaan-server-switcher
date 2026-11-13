using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Editing;

/// <summary>
/// An opened .0m file being edited: the tree, the container settings needed to write it back
/// (original seed and flags — never regenerated), the undo history and the saved baseline used for change tracking.
/// </summary>
public sealed class ConfigDocument
{
    public ConfigDocument(OmFileContent content, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Container = content.Container;
        TreeHeader = content.Tree.Header;
        Trailer = content.Tree.Trailer;
        Root = content.Tree.Root;
        LoadWarnings = content.Warnings;
        FilePath = filePath is null ? null : Path.GetFullPath(filePath);
        Baseline = TreeSnapshot.Capture(Root);
        History.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public static ConfigDocument Open(string path) => new(OmFile.Load(path), path);

    public string? FilePath { get; private set; }

    public string FileName => FilePath is null ? "untitled.0m" : Path.GetFileName(FilePath);

    /// <summary>Container settings of the opened file. Their seed and flags are reused on every save.</summary>
    public OmContainerInfo Container { get; private set; }

    public byte[] TreeHeader { get; }

    public byte[] Trailer { get; }

    public ConfigNode Root { get; }

    public IReadOnlyList<string> LoadWarnings { get; }

    public UndoStack History { get; } = new();

    public TreeSnapshot Baseline { get; private set; }

    /// <summary>Raised after every edit, undo, redo or save.</summary>
    public event EventHandler? Changed;

    public void Execute(IEditCommand command) => History.Execute(command);

    public IReadOnlyList<ConfigChange> GetChanges() => ChangeTracker.Compare(Baseline, Root);

    public bool IsModified => GetChanges().Count > 0;

    /// <summary>
    /// Reverts every change since the last save as one undoable step. Returns false when there was nothing to revert.
    /// </summary>
    public bool DiscardAllChanges()
    {
        var applied = new List<IEditCommand>();
        for (var guard = 0; guard < 100_000; guard++)
        {
            var revert = GetChanges().Select(c => c.CreateRevertCommand()).FirstOrDefault(c => c is not null);
            if (revert is null)
            {
                break;
            }

            revert.Do();
            applied.Add(revert);
        }

        if (applied.Count == 0)
        {
            return false;
        }

        History.Record(new CompositeCommand("Discard all changes", applied));
        return true;
    }

    /// <summary>The bytes that <see cref="Save"/> would write.</summary>
    public byte[] Encode() => OmFile.Encode(Container, TreeHeader, Root, Trailer);

    /// <summary>
    /// Saves to <paramref name="path"/> (or the opened file), re-encrypting with the original seed.
    /// The current tree becomes the new baseline; the undo history is kept.
    /// </summary>
    public OmSaveResult Save(string? path = null, bool createBackup = true)
    {
        var target = path ?? FilePath ?? throw new InvalidOperationException("No file path to save to.");
        var result = OmFile.Save(target, Container, TreeHeader, Root, Trailer, createBackup);
        FilePath = result.Path;
        Container = result.Container;
        Baseline = TreeSnapshot.Capture(Root);

        // Edits after a save start a new undo step, so a single Undo never jumps past the saved state.
        History.BreakMerge();
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
