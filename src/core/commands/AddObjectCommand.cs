using Godot;
using simplyRemadeNuxi.core;
using simplyRemadeNuxi;

namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Records the addition of a <see cref="SceneObject"/> to the scene so it can
/// be undone (removes the object) and redone (re-adds it).
/// </summary>
public class AddObjectCommand : IEditorCommand
{
    private readonly SceneObject _object;
    private readonly Node _parent;

    public string Description => $"Add {_object?.Name ?? "Object"}";

    /// <param name="addedObject">The object that was just added to the scene.</param>
    /// <param name="parent">The node it was added to (usually the SubViewport).</param>
    public AddObjectCommand(SceneObject addedObject, Node parent)
    {
        _object = addedObject;
        _parent = parent;
    }

    public void Execute()
    {
        if (!IsObjectValid() || !IsParentValid()) return;

        if (_object.GetParent() == null)
        {
            _parent.AddChild(_object);
        }

        RefreshSceneTree();
    }

    public void Undo()
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

    private static void RefreshSceneTree()
    {
        if (Main.Instance?.SceneTreePanel != null)
            Main.Instance.SceneTreePanel.Refresh();
    }

    private bool IsObjectValid() => _object != null && GodotObject.IsInstanceValid(_object);
    private bool IsParentValid() => _parent != null && GodotObject.IsInstanceValid(_parent);
}
