namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Represents a reversible editor action following the Command pattern.
/// Every user-facing edit (transform, spawn, delete, visibility, etc.) should
/// be wrapped in an IEditorCommand so it can be undone and redone.
/// </summary>
public interface IEditorCommand
{
    /// <summary>Human-readable description shown in the Edit menu tooltip.</summary>
    string Description { get; }

    /// <summary>Executes (or re-executes) the command.</summary>
    void Execute();

    /// <summary>Reverses the effect of <see cref="Execute"/>.</summary>
    void Undo();
}
