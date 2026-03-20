using Godot;
using simplyRemadeNuxi.core;
using simplyRemadeNuxi;

namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Records a parent change on a <see cref="SceneObject"/> so it can be undone and redone.
/// </summary>
public class ReparentCommand : IEditorCommand
{
    private readonly SceneObject _object;
    private readonly Node _oldParent;
    private readonly Node _newParent;

    public string Description => $"Reparent {_object?.Name ?? "Object"}";

    /// <param name="obj">The object being reparented.</param>
    /// <param name="oldParent">The parent before the change.</param>
    /// <param name="newParent">The parent after the change.</param>
    public ReparentCommand(SceneObject obj, Node oldParent, Node newParent)
    {
        _object = obj;
        _oldParent = oldParent;
        _newParent = newParent;
    }

    public void Execute()
    {
        if (!IsValid()) return;
        ApplyReparent(_newParent);
    }

    public void Undo()
    {
        if (!IsValid()) return;
        ApplyReparent(_oldParent);
    }

    private void ApplyReparent(Node targetParent)
    {
        if (targetParent == null || !GodotObject.IsInstanceValid(targetParent)) return;

        var globalTransform = _object.GlobalTransform;
        _object.Reparent(targetParent);
        _object.GlobalTransform = globalTransform;

        if (Main.Instance?.SceneTreePanel != null)
            Main.Instance.SceneTreePanel.Refresh();
    }

    private bool IsValid() =>
        _object != null && GodotObject.IsInstanceValid(_object) &&
        _oldParent != null && GodotObject.IsInstanceValid(_oldParent) &&
        _newParent != null && GodotObject.IsInstanceValid(_newParent);
}
