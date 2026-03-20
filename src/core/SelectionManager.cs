using Gizmo3DPlugin;
using Godot;
using Godot.Collections;
using simplyRemadeNuxi.core.commands;

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
    
    // Track if we're syncing selection (to prevent auto-keyframing during selection changes)
    private bool _isSyncingSelection = false;

    // Pre-transform state captured when the gizmo starts a drag (for undo/redo)
    private System.Collections.Generic.Dictionary<SceneObject, (Vector3 pos, Vector3 rot, Vector3 scale)>
        _preGizmoTransforms = new();
    
    [Export] public ShaderMaterial SelectionMaterial;

    public override void _Ready()
    {
        Instance = this;
    }
    
    private void OnGizmoTransformBegin(int mode)
    {
        IsGizmoEditing = true;

        // Capture pre-transform state for all selected objects
        _preGizmoTransforms.Clear();
        foreach (var obj in SelectedObjects)
        {
            Vector3 pos, rot;
            if (obj is BoneSceneObject boneObj)
            {
                pos = boneObj.TargetPosition;
                rot = boneObj.TargetRotation;
            }
            else
            {
                pos = obj.LocalPosition;
                rot = obj.LocalRotation;
            }
            _preGizmoTransforms[obj] = (pos, rot, obj.LocalScale);
        }
    }
    
    private void OnGizmoTransformEnd(int mode, int plane)
    {
    	IsGizmoEditing = false;
    	
    	// Any gizmo manipulation is a scene change → mark the project dirty
    	ProjectManager.MarkDirty();
    	
    	// Update target position/rotation for bone objects
    	foreach (var obj in SelectedObjects)
    	{
    		if (obj is BoneSceneObject boneObj)
    		{
    			boneObj.OnGizmoTransformEnd();
    		}
    	}

    	// Record undo commands for each transformed object
    	if (EditorCommandHistory.Instance != null)
    	{
    		foreach (var obj in SelectedObjects)
    		{
    			if (!_preGizmoTransforms.TryGetValue(obj, out var pre)) continue;

    			Vector3 newPos, newRot;
    			if (obj is BoneSceneObject boneObj2)
    			{
    				newPos = boneObj2.TargetPosition;
    				newRot = boneObj2.TargetRotation;
    			}
    			else
    			{
    				// Read from actual Node3D transform (not cached SceneObject properties)
    				// because Gizmo3D modifies the Node3D directly, not through SceneObject setters
    				newPos = obj.Transform.Origin;
    				newRot = obj.Rotation;
    			}
    			var newScale = obj.Scale;

    			if (newPos != pre.pos || newRot != pre.rot || newScale != pre.scale)
    			{
    				EditorCommandHistory.Instance.PushWithoutExecute(
    					new TransformCommand(obj, pre.pos, pre.rot, pre.scale,
    						newPos, newRot, newScale, "Gizmo Transform"));
    			}
    		}
    	}
    	_preGizmoTransforms.Clear();

        // Don't auto-keyframe if we're just syncing the selection (not actually transforming)
    	if (_isSyncingSelection) return;
    	
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
        
        // Set flag to prevent auto-keyframing during selection sync
        _isSyncingSelection = true;
        
        // Clear current gizmo selection
        Gizmo.ClearSelection();
        
        // Add all selected objects to the gizmo
        foreach (var obj in SelectedObjects)
        {
            Gizmo.Select(obj);
        }
        
        // Reset flag
        _isSyncingSelection = false;
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
