using System.Collections.Generic;
using Godot;

namespace simplyRemadeNuxi.core;

public partial class SceneObject : Node3D
{
	[Signal] public delegate void ParentChangedEventHandler();
	
	public string ObjectType = "Object";
	public bool IsSelectable = true;
	public bool ObjectVisible = true;
	public bool IsSelected = false;
	public Color PickColor = Colors.White;
	public string ObjectId = "";
	public int PickColorId = 0;
	
	// Keyframe storage for animation
	// Dictionary key format: "propertyPath" (e.g., "visible", "position.x", "rotation.y")
	public Dictionary<string, List<ObjectKeyframe>> Keyframes = new Dictionary<string, List<ObjectKeyframe>>();
	
	private Vector3 _pivotOffset = Vector3.Zero;
	public Vector3 PivotOffset
	{
		get => _pivotOffset;
		set
		{
			_pivotOffset = value;
			UpdateVisualPosition();
			UpdateChildrenPivotOffsets();
		}
	}
	
	public Node3D Visual;
	
	public SceneObject()
	{
		AddToGroup("SceneObject");
		Visual = new Node3D();
		Visual.Name = "Visual";
		AddChild(Visual);
		(ObjectId, PickColorId) = SelectionManager.Instance.GetNextObjectId();
		GeneratePickColor();
		UpdateVisualPosition(); // Initialize visual position with pivot offset
	}
	
	public override void _Ready()
	{
		base._Ready();
		// Update visual position after all parent-child relationships are established
		UpdateVisualPosition();
	}
	
	private void GeneratePickColor()
	{
		// This supports around 16 million objects
		var r = ((PickColorId / 65025) % 255) / 255f;
		var g = ((PickColorId / 255) % 255) / 255f;
		var b = (PickColorId % 255) / 255f;
		
		PickColor = new Color(r, g, b);
	}
	
	public string GetDisplayName()
	{
		return Name;
	}

	public string GetObjectIcon()
	{
		return "Object";
	}

	public void SetObjectVisible(bool visible)
	{
		ObjectVisible = visible;
		Visual.Visible = ObjectVisible;
	}

	public bool SetParent(SceneObject parent)
	{
		// Return true if successful. Sets the parent of an object
		if (parent == this)
		{
			GD.PrintErr("Cannot set self as parent");
			return false;
		}

		if (parent == GetParent())
		{
			return false;
		}

		// Check if the new parent is a descendant of itself
		if (parent != null)
		{
			var current = (Node)parent;
			while (current != null)
			{
				if (current == this)
				{
					GD.PrintErr("Cannot create cyclic relationship");
					return false;
				}

				current = current.GetParent();
			}
		}

		var oldParent = GetParent();

		var globalTransform = GlobalTransform;
		
		Reparent(parent);

		GlobalTransform = globalTransform;
		
		// Update pivot offset based on new parent hierarchy
		UpdateVisualPosition();
		
		EmitSignal(nameof(ParentChanged), oldParent, parent);
		return true;
	}

	public SceneObject[] GetChildrenObjects()
	{
		var children = new List<SceneObject>();

		foreach (var child in GetChildren())
		{
			if (child is SceneObject sceneObject)
			{
				children.Add(sceneObject);
			}
		}
		
		return children.ToArray();
	}
	
	public SceneObject[] GetAllDescendants()
	{
		var descendants = new List<SceneObject>();

		foreach (var child in GetChildren())
		{
			if (child is not SceneObject sceneObject) continue;
			descendants.Add(sceneObject);
			descendants.AddRange(sceneObject.GetAllDescendants());
		}
		
		return descendants.ToArray();
	}

	public void ToggleObjectVisibility()
	{
		ObjectVisible = !ObjectVisible;
		SetObjectVisible(ObjectVisible);
	}

	public void SetSelected(bool selected)
	{
		IsSelected = selected;
		ApplySelectionMaterial(selected);
	}

	public void ApplySelectionMaterial(bool selected)
	{
		if (Visual == null) return;
		
		var meshInstances = GetMeshInstancesRecursively(Visual);
		
		foreach (var meshInstance in meshInstances)
		{
			meshInstance.MaterialOverlay = selected ? SelectionManager.Instance.SelectionMaterial : null;
		}
	}

	public List<MeshInstance3D> GetMeshInstancesRecursively(Node node)
	{
		var meshInstances = new List<MeshInstance3D>();
		
		foreach (var child in node.GetChildren())
		{
			if (child is MeshInstance3D meshInstance)
			{
				meshInstances.Add(meshInstance);
			}
			
			// Recursively search through child nodes
			if (child.GetChildCount() > 0)
			{
				meshInstances.AddRange(GetMeshInstancesRecursively(child));
			}
		}
		
		return meshInstances;
	}
	
	public void AddVisualInstance(Node3D visual)
	{
		Visual.AddChild(visual);
	}

	/// <summary>
	/// Gets the accumulated pivot offset from all parent SceneObjects
	/// </summary>
	public Vector3 GetAccumulatedPivotOffset()
	{
		var accumulated = PivotOffset;
		var parent = GetParent();
		
		if (parent is SceneObject parentSceneObject)
		{
			accumulated += parentSceneObject.GetAccumulatedPivotOffset();
		}
		
		return accumulated;
	}
	
	private void UpdateVisualPosition()
	{
		if (Visual != null)
		{
			// Apply the accumulated pivot offset from this object and all parents
			Visual.Position = -GetAccumulatedPivotOffset();
		}
	}
	
	private void UpdateChildrenPivotOffsets()
	{
		// Recursively update all children when this object's pivot changes
		foreach (var child in GetChildrenObjects())
		{
			child.UpdateVisualPosition();
			child.UpdateChildrenPivotOffsets();
		}
	}
}

/// <summary>
/// Represents a keyframe stored with a SceneObject
/// </summary>
public class ObjectKeyframe
{
	public int Frame { get; set; }
	public object Value { get; set; }
	public string InterpolationType { get; set; } = "linear";
}