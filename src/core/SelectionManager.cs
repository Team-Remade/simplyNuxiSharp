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
    
    private int NextObjectId = 1;
    
    [Export] public ShaderMaterial SelectionMaterial;

    public override void _Ready()
    {
        Instance = this;
    }

    public int GetNextObjectId()
    {
        //TODO: Handle object deletions
        var id = NextObjectId;
        NextObjectId++;
        return id;
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

    public bool IsSelected(SceneObject obj)
    {
        return SelectedObjects.Contains(obj);
    }
}