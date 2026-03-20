using Godot;
using simplyRemadeNuxi.core;
using simplyRemadeNuxi;

namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Records the deletion of a <see cref="SceneObject"/> from the scene so it
/// can be undone (re-adds the object) and redone (removes it again).
/// </summary>
public class DeleteObjectCommand : IEditorCommand
{
    private readonly SceneObject _object;
    private readonly Node _parent;

    public string Description => $"Delete {_object?.Name ?? "Object"}";

    /// <param name="objectToDelete">The object that is about to be (or was just) deleted.</param>
    /// <param name="parent">The node it belongs to (usually the SubViewport).</param>
    public DeleteObjectCommand(SceneObject objectToDelete, Node parent)
    {
        _object = objectToDelete;
        _parent = parent;
    }

    public void Execute()
    {
        if (!IsObjectValid()) return;

        // Deselect before removing
        if (SelectionManager.Instance != null &&
            SelectionManager.Instance.SelectedObjects.Contains(_object))
        {
            SelectionManager.Instance.DeselectObject(_object);
        }

        if (_object.GetParent() != null)
        {
            _object.GetParent().RemoveChild(_object);
        }

        RefreshSceneTree();
    }

    public void Undo()
    {
        if (!IsObjectValid() || !IsParentValid()) return;

        if (_object.GetParent() == null)
        {
            _parent.AddChild(_object);
        }

        RefreshSceneTree();
    }

    private static void RefreshSceneTree()
    {
        if (Main.Instance?.SceneTreePanel != null)
            Main.Instance.SceneTreePanel.Refresh();
    }

    private bool IsObjectValid() => _object != null && GodotObject.IsInstanceValid(_object);
    private bool IsParentValid() => _parent != null && GodotObject.IsInstanceValid(_parent);
}
