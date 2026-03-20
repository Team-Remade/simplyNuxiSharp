using Godot;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.core.commands;

/// <summary>
/// Records a position, rotation, or scale change on a <see cref="SceneObject"/>
/// so it can be undone and redone.
/// </summary>
public class TransformCommand : IEditorCommand
{
    private readonly SceneObject _target;
    private readonly Vector3 _oldPosition;
    private readonly Vector3 _oldRotation;
    private readonly Vector3 _oldScale;
    private readonly Vector3 _newPosition;
    private readonly Vector3 _newRotation;
    private readonly Vector3 _newScale;

    public string Description { get; }

    /// <param name="target">The object whose transform changed.</param>
    /// <param name="oldPosition">Local position before the change.</param>
    /// <param name="oldRotation">Local rotation (radians) before the change.</param>
    /// <param name="oldScale">Local scale before the change.</param>
    /// <param name="newPosition">Local position after the change.</param>
    /// <param name="newRotation">Local rotation (radians) after the change.</param>
    /// <param name="newScale">Local scale after the change.</param>
    /// <param name="description">Optional human-readable label (defaults to "Transform").</param>
    public TransformCommand(
        SceneObject target,
        Vector3 oldPosition, Vector3 oldRotation, Vector3 oldScale,
        Vector3 newPosition, Vector3 newRotation, Vector3 newScale,
        string description = "Transform")
    {
        _target = target;
        _oldPosition = oldPosition;
        _oldRotation = oldRotation;
        _oldScale = oldScale;
        _newPosition = newPosition;
        _newRotation = newRotation;
        _newScale = newScale;
        Description = description;
    }

    public void Execute()
    {
        if (!IsValid()) return;
        ApplyTransform(_newPosition, _newRotation, _newScale);
    }

    public void Undo()
    {
        if (!IsValid()) return;
        ApplyTransform(_oldPosition, _oldRotation, _oldScale);
    }

    private void ApplyTransform(Vector3 pos, Vector3 rot, Vector3 scale)
    {
        if (_target is BoneSceneObject boneObj)
        {
            boneObj.TargetPosition = pos;
            boneObj.TargetRotation = rot;
            _target.SetLocalScale(scale);
        }
        else
        {
            _target.SetLocalPosition(pos);
            _target.SetLocalRotation(rot);
            _target.SetLocalScale(scale);
        }

        // Refresh the properties panel if this object is currently selected
        if (ObjectPropertiesPanel.Instance != null &&
            SelectionManager.Instance != null &&
            SelectionManager.Instance.SelectedObjects.Contains(_target))
        {
            ObjectPropertiesPanel.Instance.RefreshFromObject();
        }
    }

    private bool IsValid() => _target != null && GodotObject.IsInstanceValid(_target);
}
