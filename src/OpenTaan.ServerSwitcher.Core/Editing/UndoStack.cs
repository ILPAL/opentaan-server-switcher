namespace OpenTaan.ServerSwitcher.Core.Editing;

/// <summary>Linear undo/redo history of <see cref="IEditCommand"/>s.</summary>
public sealed class UndoStack
{
    private readonly List<IEditCommand> _undo = new();
    private readonly List<IEditCommand> _redo = new();
    private bool _mergeAllowed;

    public UndoStack(int capacity = 500)
    {
        Capacity = capacity;
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => CanUndo ? _undo[^1].Description : null;

    public string? RedoDescription => CanRedo ? _redo[^1].Description : null;

    /// <summary>Raised after Execute, Undo, Redo or Clear.</summary>
    public event EventHandler? Changed;

    /// <summary>Runs <paramref name="command"/> and records it, merging it into the previous step when possible.</summary>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Do();
        _redo.Clear();

        if (_mergeAllowed && _undo.Count > 0 && _undo[^1] is IMergeableEditCommand top && top.TryMerge(command))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _undo.Add(command);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        _mergeAllowed = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records a command whose effect has already been applied (no <see cref="IEditCommand.Do"/> call).</summary>
    public void Record(IEditCommand executed)
    {
        ArgumentNullException.ThrowIfNull(executed);
        _redo.Clear();
        _undo.Add(executed);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        _mergeAllowed = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends the current merge run: the next command starts a new undo step (call on focus/selection change).</summary>
    public void BreakMerge() => _mergeAllowed = false;

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add(command);
        _mergeAllowed = false;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Do();
        _undo.Add(command);
        _mergeAllowed = false;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _mergeAllowed = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
