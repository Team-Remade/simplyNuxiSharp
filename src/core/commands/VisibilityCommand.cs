using Godot;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Records a visibility toggle on a <see cref="SceneObject"/> so it can be undone and redone.
/// </summary>
public class VisibilityCommand : IEditorCommand
{
    private readonly SceneObject _target;
    private readonly bool _oldVisible;
    private readonly bool _newVisible;

    public string Description => _newVisible ? "Show Object" : "Hide Object";

    public VisibilityCommand(SceneObject target, bool oldVisible, bool newVisible)
    {
        _target = target;
        _oldVisible = oldVisible;
        _newVisible = newVisible;
    }

    public void Execute()
    {
        if (!IsValid()) return;
        _target.SetObjectVisible(_newVisible);
        RefreshUi();
    }

    public void Undo()
    {
        if (!IsValid()) return;
        _target.SetObjectVisible(_oldVisible);
        RefreshUi();
    }

    private void RefreshUi()
    {
        if (ObjectPropertiesPanel.Instance != null &&
            SelectionManager.Instance != null &&
            SelectionManager.Instance.SelectedObjects.Contains(_target))
        {
            ObjectPropertiesPanel.Instance.RefreshFromObject();
        }
    }

    private bool IsValid() => _target != null && GodotObject.IsInstanceValid(_target);
}
