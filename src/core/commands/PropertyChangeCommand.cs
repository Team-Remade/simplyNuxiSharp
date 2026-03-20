using System;

namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Generic command that records a single property change of type <typeparamref name="T"/>.
/// The caller provides apply/revert delegates so this class stays decoupled from any
/// specific panel or object type.
/// </summary>
public class PropertyChangeCommand<T> : IEditorCommand
{
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action<T> _apply;

    public string Description { get; }

    /// <param name="description">Human-readable label (e.g. "Change Pivot Offset").</param>
    /// <param name="oldValue">Value before the change.</param>
    /// <param name="newValue">Value after the change.</param>
    /// <param name="apply">Delegate that applies a value (used for both Execute and Undo).</param>
    public PropertyChangeCommand(string description, T oldValue, T newValue, Action<T> apply)
    {
        Description = description;
        _oldValue = oldValue;
        _newValue = newValue;
        _apply = apply;
    }

    public void Execute() => _apply(_newValue);
    public void Undo()    => _apply(_oldValue);
}
