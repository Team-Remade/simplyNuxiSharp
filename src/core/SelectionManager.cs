using Gizmo3DPlugin;
using Godot;
using Godot.Collections;

namespace simplyRemadeNuxi.core;

public partial class SelectionManager : Node
{
    public static SelectionManager Instance;
    
    [Signal] public delegate void SelectionChangedEventHandler();
    
    public Array<SceneObject> SelectedObjects = new Array<SceneObject>();
    
    public Gizmo3D Gizmo;
    
    private int NextPickColorId = 1;
    
    // Track if gizmo is being used to prevent timeline from overriding transforms
    public bool IsGizmoEditing { get; private set; } = false;
    
    [Export] public ShaderMaterial SelectionMaterial;

    public override void _Ready()
    {
        Instance = this;
    }
    
    private void OnGizmoTransformBegin(int mode)
    {
        IsGizmoEditing = true;
    }
    
    private void OnGizmoTransformEnd(int mode, int plane)
    {
        IsGizmoEditing = false;
        
        // Auto-keyframe affected properties when gizmo manipulation ends
        if (TimelinePanel.Instance == null || SelectedObjects.Count == 0) return;
        
        var transformMode = (Gizmo3D.TransformMode)mode;
        var transformPlane = (Gizmo3D.TransformPlane)plane;
        
        foreach (var obj in SelectedObjects)
        {
            string propertyPrefix = transformMode switch
            {
                Gizmo3D.TransformMode.Translate => "position",
                Gizmo3D.TransformMode.Rotate => "rotation",
                Gizmo3D.TransformMode.Scale => "scale",
                _ => null
            };
            
            if (propertyPrefix == null) continue;
            
            // Keyframe only the affected axes based on the plane/axis that was manipulated
            switch (transformPlane)
            {
                case Gizmo3D.TransformPlane.X:
                    // Single axis: X only
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.x", TimelinePanel.Instance.CurrentFrame);
                    break;
                    
                case Gizmo3D.TransformPlane.Y:
                    // Single axis: Y only
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.y", TimelinePanel.Instance.CurrentFrame);
                    break;
                    
                case Gizmo3D.TransformPlane.Z:
                    // Single axis: Z only
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.z", TimelinePanel.Instance.CurrentFrame);
                    break;
                    
                case Gizmo3D.TransformPlane.YZ:
                    // Plane: Y and Z
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.y", TimelinePanel.Instance.CurrentFrame);
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.z", TimelinePanel.Instance.CurrentFrame);
                    break;
                    
                case Gizmo3D.TransformPlane.XZ:
                    // Plane: X and Z
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.x", TimelinePanel.Instance.CurrentFrame);
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.z", TimelinePanel.Instance.CurrentFrame);
                    break;
                    
                case Gizmo3D.TransformPlane.XY:
                    // Plane: X and Y
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.x", TimelinePanel.Instance.CurrentFrame);
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.y", TimelinePanel.Instance.CurrentFrame);
                    break;
                    
                case Gizmo3D.TransformPlane.View:
                    // View plane: All axes
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.x", TimelinePanel.Instance.CurrentFrame);
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.y", TimelinePanel.Instance.CurrentFrame);
                    TimelinePanel.Instance.AddKeyframeForProperty(obj, $"{propertyPrefix}.z", TimelinePanel.Instance.CurrentFrame);
                    break;
            }
        }
    }

    public (string uuid, int pickColorId) GetNextObjectId()
    {
        var uuid = System.Guid.NewGuid().ToString();
        var pickColorId = NextPickColorId;
        NextPickColorId++;
        return (uuid, pickColorId);
    }

    public void SelectObject(SceneObject obj)
    {
        if (SelectedObjects.Contains(obj)) return;

        if (!obj.IsSelectable) return;
        
        SelectedObjects.Add(obj);
        obj.SetSelected(true);
        ApplySelection(obj, true);
        SyncGizmoSelection();
        EmitSignal(nameof(SelectionChanged));
    }

    public void DeselectObject(SceneObject obj)
    {
        if (!SelectedObjects.Contains(obj)) return;
        SelectedObjects.Remove(obj);
        obj.SetSelected(false);
        ApplySelection(obj, false);
        SyncGizmoSelection();
        EmitSignal(nameof(SelectionChanged));
    }

    public void ToggleSelection(SceneObject obj)
    {
        if (SelectedObjects.Contains(obj))
        {
            DeselectObject(obj);
        }
        else
        {
            SelectObject(obj);
        }
    }

    public void ClearSelection()
    {
        foreach (var obj in SelectedObjects)
        {
            obj.SetSelected(false);
            ApplySelection(obj, false);
        }
        SelectedObjects.Clear();
        SyncGizmoSelection();
        EmitSignal(nameof(SelectionChanged));
    }
    
    private void ApplySelection(SceneObject obj, bool selected)
    {
        obj.ApplySelectionMaterial(selected);
    }

    /// <summary>
    /// Sync the gizmo selection with the currently selected SceneObjects.
    /// The gizmo will attach to all selected objects and position itself at their center.
    /// </summary>
    private void SyncGizmoSelection()
    {
        if (Gizmo == null) return;
        
        // Clear current gizmo selection
        Gizmo.ClearSelection();
        
        // Add all selected objects to the gizmo
        foreach (var obj in SelectedObjects)
        {
            Gizmo.Select(obj);
        }
    }

    /// <summary>
    /// Connects to gizmo signals for auto-keyframing. Call this after the Gizmo is initialized.
    /// </summary>
    public void ConnectGizmoSignals()
    {
        if (Gizmo == null)
        {
            GD.PrintErr("[SelectionManager] Cannot connect gizmo signals - Gizmo is null");
            return;
        }
        
        Gizmo.TransformBegin += OnGizmoTransformBegin;
        Gizmo.TransformEnd += OnGizmoTransformEnd;
    }
    
    public bool IsSelected(SceneObject obj)
    {
        return SelectedObjects.Contains(obj);
    }
}