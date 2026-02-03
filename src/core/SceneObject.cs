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
	public int ObjectId = 0;
	
	private Node3D Visual;
	
	public SceneObject()
	{
		AddToGroup("SceneObject");
		Visual = new Node3D();
		Visual.Name = "Visual";
		AddChild(Visual);
		ObjectId = -1; //TODO: Assign a proper id on creation
		GeneratePickColor();
	}
	
	private void GeneratePickColor()
	{
		// This supports around 16 million objects
		var r = ((ObjectId / 65025f) % 255) / 255f;
		var g = ((ObjectId / 255f) % 255) / 255f;
		var b = (ObjectId % 255) / 255f;
		
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

	private void ApplySelectionMaterial(bool selected)
	{
		
	}
}