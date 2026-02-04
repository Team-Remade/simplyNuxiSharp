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
        ApplySelection(obj, true);
        EmitSignal(nameof(SelectionChanged));
    }

    public void DeselectObject(SceneObject obj)
    {
        if (!SelectedObjects.Contains(obj)) return;
        SelectedObjects.Remove(obj);
        
        ApplySelection(obj, false);
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
            ApplySelection(obj, false);
        }
        SelectedObjects.Clear();
        EmitSignal(nameof(SelectionChanged));
    }
    
    private void ApplySelection(SceneObject obj, bool selected)
    {
        obj.ApplySelectionMaterial(selected);
    }

    public bool IsSelected(SceneObject obj)
    {
        return SelectedObjects.Contains(obj);
    }
}