using System.Collections.Generic;
using Godot;

namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Singleton that manages the undo/redo history for all editor commands.
///
/// Internally uses a flat <see cref="List{T}"/> of commands plus a cursor
/// (<see cref="_cursor"/>) that points to the last executed command.
///
/// <list type="bullet">
///   <item>Cursor == -1  → nothing has been done yet (or history was cleared)</item>
///   <item>Cursor == N   → commands[0..N] have been executed; commands[N+1..] are redo-able</item>
/// </list>
///
/// When a new command is recorded any redo-able commands (after the cursor) are
/// discarded, then the new command is appended and the cursor advances.
/// </summary>
public partial class EditorCommandHistory : Node
{
    public static EditorCommandHistory Instance { get; private set; }

    private readonly List<IEditorCommand> _history = new();
    private int _cursor = -1;

    /// <summary>Maximum number of commands kept in the history list.</summary>
    private const int MaxHistorySize = 200;

    /// <summary>Fired whenever the history changes so the UI can update its state.</summary>
    [Signal] public delegate void HistoryChangedEventHandler();

    public bool CanUndo => _cursor >= 0;
    public bool CanRedo => _cursor < _history.Count - 1;

    public string UndoDescription => CanUndo ? _history[_cursor].Description : string.Empty;
    public string RedoDescription => CanRedo ? _history[_cursor + 1].Description : string.Empty;

    public override void _Ready()
    {
        Instance = this;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes <paramref name="command"/>, appends it to the history at the
    /// current cursor position (discarding any redo-able commands), and advances
    /// the cursor.
    /// </summary>
    public void Execute(IEditorCommand command)
    {
        command.Execute();
        Record(command);
    }

    /// <summary>
    /// Appends <paramref name="command"/> to the history WITHOUT calling
    /// <see cref="IEditorCommand.Execute"/>.  Use this when the action has
    /// already been applied (e.g. a spinbox value was changed by the user) and
    /// you only need to record it so it can be undone later.
    /// </summary>
    public void PushWithoutExecute(IEditorCommand command) => Record(command);

    /// <summary>
    /// Undoes the command at the current cursor position and moves the cursor back.
    /// </summary>
    public void Undo()
    {
        if (!CanUndo) return;

        _history[_cursor].Undo();
        _cursor--;

        EmitSignal(SignalName.HistoryChanged);
    }

    /// <summary>
    /// Re-executes the command just after the cursor and advances the cursor.
    /// </summary>
    public void Redo()
    {
        if (!CanRedo) return;

        _cursor++;
        _history[_cursor].Execute();

        EmitSignal(SignalName.HistoryChanged);
    }

    /// <summary>
    /// Clears the entire history (e.g. when a new project is opened).
    /// </summary>
    public void Clear()
    {
        _history.Clear();
        _cursor = -1;
        EmitSignal(SignalName.HistoryChanged);
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private void Record(IEditorCommand command)
    {
        // Discard any redo-able commands after the cursor
        if (_cursor < _history.Count - 1)
            _history.RemoveRange(_cursor + 1, _history.Count - _cursor - 1);

        _history.Add(command);
        _cursor = _history.Count - 1;

        // Trim the oldest entries if the list grows too large
        if (_history.Count > MaxHistorySize)
        {
            int excess = _history.Count - MaxHistorySize;
            _history.RemoveRange(0, excess);
            _cursor -= excess;
            if (_cursor < -1) _cursor = -1;
        }

        EmitSignal(SignalName.HistoryChanged);
    }
}
