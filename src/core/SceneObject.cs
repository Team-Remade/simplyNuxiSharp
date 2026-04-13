using System.Collections.Generic;
using System.Linq;
using Godot;

namespace simplyRemadeNuxi.core;

public class MaterialSettings
{
	public Color AlbedoColor = new Color(1, 1, 1, 1);
	public float Metallic = 0f;
	public float Roughness = 0.5f;
	public bool NormalEnabled = false;
	public Texture2D NormalTexture = null;
	public BaseMaterial3D.TransparencyEnum Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
	public bool EmissionEnabled = false;
	public Color EmissionColor = new Color(0, 0, 0, 1);
	public float EmissionEnergy = 1f;
}

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

	/// <summary>
	/// Absolute path to the source asset file that was used to create this object.
	/// Set by SpawnMenu when loading a custom model so the project save system can
	/// serialise and restore it.  Empty for built-in objects (primitives, lights, etc.).
	/// </summary>
	public string SourceAssetPath = "";

	/// <summary>
	/// The spawn category this object belongs to (e.g. "Primitives", "Blocks", "Items",
	/// "Light", "Characters", "Custom Models").  Set by SpawnMenu.
	/// </summary>
	public string SpawnCategory = "";

	/// <summary>
	/// For Blocks: the blockstate variant string (e.g. "facing=north").
	/// </summary>
	public string BlockVariant = "";

	/// <summary>
	/// For Items: the texture type ("block" or "item").
	/// </summary>
	public string TextureType = "item";

	/// <summary>
	/// Material settings that can be inherited from parent SceneObjects.
	/// When null, this object uses its own defaults or explicit material settings.
	/// </summary>
	private MaterialSettings _materialSettings;
	public MaterialSettings MaterialSettings
	{
		get => _materialSettings;
		set
		{
			if (_materialSettings != value)
			{
				_materialSettings = value;
				OnMaterialSettingsChanged();
			}
		}
	}

	/// <summary>
	/// Called when MaterialSettings changes (either directly or via inheritance).
	/// Propagates the change to all child SceneObjects.
	/// </summary>
	private void OnMaterialSettingsChanged()
	{
		PropagateMaterialSettingsToChildren();
	}

	/// <summary>
	/// Propagates this object's MaterialSettings to all descendant SceneObjects
	/// that don't have their own explicit MaterialSettings.
	/// </summary>
	public void PropagateMaterialSettingsToChildren()
	{
		if (_materialSettings == null) return;
		
		foreach (var child in GetChildren())
		{
			if (child is SceneObject childObj)
			{
				if (!childObj._hasExplicitMaterialSettings)
				{
					childObj._materialSettings = _materialSettings;
					childObj.ApplyMaterialSettingsToMeshes();
					childObj.PropagateMaterialSettingsToChildren();
				}
			}
		}
	}

	/// <summary>
	/// Flag indicating whether this object has explicitly set its own MaterialSettings
	/// (as opposed to inheriting from parent). Children with explicit settings don't get updated.
	/// </summary>
	protected bool _hasExplicitMaterialSettings = false;

	/// <summary>
	/// Marks this object's MaterialSettings as explicitly set (not inherited).
	/// </summary>
	public void SetExplicitMaterialSettings()
	{
		_hasExplicitMaterialSettings = _materialSettings != null;
	}

	/// <summary>
	/// Applies this object's MaterialSettings to all meshes in its Visual hierarchy.
	/// Skips mesh instances that are part of the picking/bone visual system.
	/// </summary>
	public void ApplyMaterialSettingsToMeshes()
	{
		if (_materialSettings == null)
		{
			return;
		}

		var meshes = GetMeshInstancesRecursively(Visual);
		foreach (var meshInstance in meshes.Where(m => m.Mesh != null))
		{
			// Skip bone picking visual (layer 2 indicates picking mesh)
			if ((meshInstance.Layers & 2) != 0)
				continue;

			for (int i = 0; i < meshInstance.Mesh.GetSurfaceCount(); i++)
			{
				var material = meshInstance.Mesh.SurfaceGetMaterial(i);
				if (material is StandardMaterial3D stdMat)
				{
					stdMat.AlbedoColor = _materialSettings.AlbedoColor;
					stdMat.Metallic = _materialSettings.Metallic;
					stdMat.Roughness = _materialSettings.Roughness;
					stdMat.NormalEnabled = _materialSettings.NormalEnabled;
					stdMat.NormalTexture = _materialSettings.NormalTexture;
					stdMat.Transparency = _materialSettings.Transparency;
					stdMat.EmissionEnabled = _materialSettings.EmissionEnabled;
					stdMat.Emission = _materialSettings.EmissionColor;
					stdMat.EmissionEnergyMultiplier = _materialSettings.EmissionEnergy;
					stdMat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Always;
				}
			}
		}
	}

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

	/// <summary>
	/// When true, this object accumulates the parent's pivot offset into its own visual position.
	/// When false, the parent's pivot offset is ignored (default: false).
	/// </summary>
	private bool _inheritPivotOffset = false;
	public bool InheritPivotOffset
	{
		get => _inheritPivotOffset;
		set
		{
			_inheritPivotOffset = value;
			UpdateVisualPosition();
			UpdateChildrenPivotOffsets();
		}
	}

	/// <summary>
	/// When true, this object inherits the parent's position (default: true).
	/// </summary>
	public bool InheritPosition = true;

	/// <summary>
	/// When true, this object inherits the parent's rotation (default: true).
	/// </summary>
	public bool InheritRotation = true;

	/// <summary>
	/// When true, this object inherits the parent's scale (default: true).
	/// </summary>
	public bool InheritScale = true;

	/// <summary>
	/// When > 0, this object follows the parent's bend transform.
	/// Set by the importer for parts that should bend with their parent.
	/// </summary>
	[Export] public float LockBend = 1f;

	/// <summary>
	/// When true, this object inherits the parent's visibility (default: true).
	/// When false, the object's own ObjectVisible property determines visibility
	/// without considering parent visibility.
	/// </summary>
	private bool _inheritVisibility = true;
	public bool InheritVisibility
	{
		get => _inheritVisibility;
		set
		{
			_inheritVisibility = value;
			ApplyEffectiveVisibility();
		}
	}

	public Node3D Visual;

	/// <summary>
	/// Controls whether this object casts shadows. Default is On.
	/// </summary>
	private GeometryInstance3D.ShadowCastingSetting _castShadow = GeometryInstance3D.ShadowCastingSetting.On;
	public GeometryInstance3D.ShadowCastingSetting CastShadow
	{
		get => _castShadow;
		set
		{
			_castShadow = value;
			ApplyCastShadow();
		}
	}

	// Stores the local transform set by the user (position/rotation/scale before inheritance is applied)
	[Export] private Vector3 _localPosition = Vector3.Zero;
	[Export] private Vector3 _localRotation = Vector3.Zero;
	[Export] private Vector3 _localScale = Vector3.One;
	
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
		// Sync local transform cache from current node transform
		_localPosition = Position;
		_localRotation = Rotation;
		_localScale = Scale;
	}

	public override void _Process(double delta)
	{
		ApplyInheritanceTransform();
	}

	/// <summary>
	/// Applies per-component transform inheritance from the parent SceneObject.
	/// When all three are true this is equivalent to normal Godot parenting.
	/// When all three are false the object behaves as a top-level node.
	/// </summary>
	private void ApplyInheritanceTransform()
	{
		BoneSceneObject bendAncestor = null;

		if (LockBend > 0f)
		{
			bendAncestor = GetBendAncestor();
		}

		// If all components are inherited and no bend, use normal Godot parenting (TopLevel = false)
		if (InheritPosition && InheritRotation && InheritScale && bendAncestor == null)
		{
			if (TopLevel)
			{
				TopLevel = false;
				// Restore local transform
				Position = _localPosition;
				Rotation = _localRotation;
				Scale = _localScale;
			}
			return;
		}

		// At least one component is not inherited OR we have a bend — switch to TopLevel and manually compose
		if (!TopLevel)
		{
			// Cache current local transform before switching to TopLevel
			_localPosition = Position;
			_localRotation = Rotation;
			_localScale = Scale;
			TopLevel = true;
		}

		var parent = GetParent() as Node3D;
		if (parent == null)
		{
			// No parent — just apply local transform directly
			Position = _localPosition;
			Rotation = _localRotation;
			Scale = _localScale;
			return;
		}

		var bendTransform = Transform3D.Identity;
		
		if (bendAncestor != null)
		{
			var pivotOffset = -GetAccumulatedPivotOffset();
			bendTransform = bendAncestor.GetBentHalfTransform(pivotOffset);
		}

		var parentGlobalPos = parent.GlobalPosition;
		var parentGlobalRot = parent.GlobalRotation;
		var parentGlobalScale = parent.GlobalTransform.Basis.Scale;

		// Build the world-space transform by selectively inheriting components
		var worldPos = InheritPosition ? parentGlobalPos + _localPosition : _localPosition;
		var worldRot = InheritRotation ? parentGlobalRot + _localRotation : _localRotation;
		var worldScale = InheritScale
			? new Vector3(parentGlobalScale.X * _localScale.X, parentGlobalScale.Y * _localScale.Y, parentGlobalScale.Z * _localScale.Z)
			: _localScale;

		var composedTransform = new Transform3D(
			Basis.FromEuler(worldRot) * Basis.FromScale(worldScale),
			worldPos);
		
		// Skip bend application if selected to allow transform gizmo to work
		if (bendAncestor != null && IsSelected)
		{
			var worldToBend = (parent.GlobalTransform * bendTransform).AffineInverse();
			var bentLocal = worldToBend * GlobalTransform;
			_localPosition = bentLocal.Origin;
			_localRotation = bentLocal.Basis.GetEuler();
			_localScale = bentLocal.Basis.Scale;
			return;
		}

		// Apply bend transformation: bend at the bend ancestor's pivot, then apply local offset
		if (bendAncestor != null)
		{
			var localTransform = new Transform3D(
				Basis.FromEuler(_localRotation) * Basis.FromScale(_localScale),
				_localPosition);
			GlobalTransform = parent.GlobalTransform * bendTransform * localTransform;
		}
		else
		{
			GlobalTransform = composedTransform;
		}
	}

	private BoneSceneObject GetBendAncestor()
	{
		var parent = GetParent();
		// GML: lock_bend defaults to true. When true, the child transforms with the parent's
		// bent-half transform (LockBend > 0 in C# = locked = apply bend matrix to child).
		if (parent is BoneSceneObject bone && bone.BendParameters.HasValue)
		{
			return bone;
		}
		return null;
	}

	/// <summary>
	/// Sets the local position and keeps the cache in sync.
	/// Use this instead of setting Position directly when inheritance may be active.
	/// </summary>
	public void SetLocalPosition(Vector3 pos)
	{
		_localPosition = pos;
		if (!TopLevel)
			Position = pos;
	}

	/// <summary>
	/// Sets the local rotation and keeps the cache in sync.
	/// Use this instead of setting Rotation directly when inheritance may be active.
	/// </summary>
	public void SetLocalRotation(Vector3 rot)
	{
		_localRotation = rot;
		if (!TopLevel)
			Rotation = rot;
	}

	/// <summary>
	/// Sets the local scale and keeps the cache in sync.
	/// Use this instead of setting Scale directly when inheritance may be active.
	/// </summary>
	public void SetLocalScale(Vector3 scale)
	{
		_localScale = scale;
		if (!TopLevel)
			Scale = scale;
	}

	/// <summary>
	/// Gets the local position (the value the user set, before inheritance is applied).
	/// </summary>
	public Vector3 LocalPosition => _localPosition;

	/// <summary>
	/// Gets the local rotation (the value the user set, before inheritance is applied).
	/// </summary>
	public Vector3 LocalRotation => _localRotation;

	/// <summary>
	/// Gets the local scale (the value the user set, before inheritance is applied).
	/// </summary>
	public Vector3 LocalScale => _localScale;
	
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
		ApplyEffectiveVisibility();
	}

	/// <summary>
	/// Gets the effective visibility of this object, considering parent visibility
	/// and the InheritVisibility setting.
	/// </summary>
	public bool GetEffectiveVisibility()
	{
		if (!InheritVisibility)
		{
			// Not inheriting - use own visibility
			return ObjectVisible;
		}

		// Check parent's effective visibility
		var parent = GetParent();
		if (parent is SceneObject parentSceneObject)
		{
			return ObjectVisible && parentSceneObject.GetEffectiveVisibility();
		}

		// No SceneObject parent - just use own visibility
		return ObjectVisible;
	}

	/// <summary>
	/// Applies the effective visibility to the visual node based on inheritance.
	/// </summary>
	private void ApplyEffectiveVisibility()
	{
		Visual.Visible = GetEffectiveVisibility();
		// Also update all children to reflect the new effective visibility
		UpdateChildrenVisibility();
	}

	private void UpdateChildrenVisibility()
	{
		foreach (var child in GetChildrenObjects())
		{
			// Re-apply visibility to children since parent's visibility changed
			child.ApplyEffectiveVisibility();
		}
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

		// Update visibility based on new parent hierarchy
		ApplyEffectiveVisibility();

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

	/// <summary>
	/// Applies the CastShadow setting to all mesh instances in the Visual hierarchy.
	/// </summary>
	public void ApplyCastShadow()
	{
		if (Visual == null) return;

		var meshInstances = GetMeshInstancesRecursively(Visual);
		foreach (var meshInstance in meshInstances)
		{
			meshInstance.CastShadow = _castShadow;
		}
	}
	
	public void AddVisualInstance(Node3D visual)
	{
		Visual.AddChild(visual);
	}

	/// <summary>
	/// Gets the accumulated pivot offset from all parent SceneObjects.
	/// Only accumulates parent offsets when InheritPivotOffset is true.
	/// </summary>
	public Vector3 GetAccumulatedPivotOffset()
	{
		var accumulated = PivotOffset;
		
		if (InheritPivotOffset)
		{
			var parent = GetParent();
			if (parent is SceneObject parentSceneObject)
			{
				accumulated += parentSceneObject.GetAccumulatedPivotOffset();
			}
		}
		
		return accumulated;
	}

	public void UpdateVisualPosition()
	{
		if (Visual == null) return;

		var pivotOffset = -GetAccumulatedPivotOffset();
		Visual.Position = pivotOffset;
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
