namespace CT320B.LabelDesigner.Core.Editing;

/// <summary>A reversible edit. <see cref="Do"/> applies it; <see cref="Undo"/> reverts it.</summary>
public interface IUndoableCommand
{
    /// <summary>Short human-readable label (e.g. for an "Undo Move" menu item).</summary>
    string Name { get; }

    /// <summary>Apply (or re-apply, on redo) the edit.</summary>
    void Do();

    /// <summary>Revert the edit.</summary>
    void Undo();
}

/// <summary>
/// A classic undo/redo stack. <see cref="Execute"/> runs a command and records it;
/// <see cref="PushExecuted"/> records a command whose effect has already been applied (used for
/// live canvas drags). Any new edit clears the redo history. Pure model logic — no UI dependency.
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    /// <summary>Raised after any change to the stack (execute/undo/redo/clear).</summary>
    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Name of the command that would be undone next, or null.</summary>
    public string? UndoName => _undo.Count > 0 ? _undo.Peek().Name : null;

    /// <summary>Name of the command that would be redone next, or null.</summary>
    public string? RedoName => _redo.Count > 0 ? _redo.Peek().Name : null;

    /// <summary>Runs <paramref name="command"/>'s <see cref="IUndoableCommand.Do"/> and records it.</summary>
    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Do();
        _undo.Push(command);
        _redo.Clear();
        OnChanged();
    }

    /// <summary>Records a command whose effect is already applied (does not call
    /// <see cref="IUndoableCommand.Do"/>). Use for edits performed live, e.g. a drag gesture.</summary>
    public void PushExecuted(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _undo.Push(command);
        _redo.Clear();
        OnChanged();
    }

    /// <summary>Undoes the most recent command.</summary>
    public void Undo()
    {
        if (_undo.Count == 0) return;
        IUndoableCommand cmd = _undo.Pop();
        cmd.Undo();
        _redo.Push(cmd);
        OnChanged();
    }

    /// <summary>Redoes the most recently undone command.</summary>
    public void Redo()
    {
        if (_redo.Count == 0) return;
        IUndoableCommand cmd = _redo.Pop();
        cmd.Do();
        _undo.Push(cmd);
        OnChanged();
    }

    /// <summary>Clears all history.</summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0) return;
        _undo.Clear();
        _redo.Clear();
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>A command defined by two delegates — convenient for one-off edits (property changes,
/// visibility/lock toggles, z-order tweaks) where typed commands would be overkill.</summary>
public sealed class DelegateCommand(string name, Action doAction, Action undoAction) : IUndoableCommand
{
    private readonly Action _do = doAction ?? throw new ArgumentNullException(nameof(doAction));
    private readonly Action _undo = undoAction ?? throw new ArgumentNullException(nameof(undoAction));

    public string Name { get; } = name;
    public void Do() => _do();
    public void Undo() => _undo();
}
