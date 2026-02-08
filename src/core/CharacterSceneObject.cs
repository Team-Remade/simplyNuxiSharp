using Godot;
using System.Collections.Generic;

namespace simplyRemadeNuxi.core;

/// <summary>
/// A specialized SceneObject for characters with rigged bones.
/// Each bone in the character's skeleton becomes its own SceneObject in the scene hierarchy.
/// </summary>
public partial class CharacterSceneObject : SceneObject
{
	// The skinned mesh that is controlled by the bones
	public MeshInstance3D SkinnedMesh { get; private set; }
	
	// Dictionary mapping bone names to their corresponding SceneObjects
	public Dictionary<string, BoneSceneObject> BoneObjects { get; private set; }
	
	// The skeleton that manages the bones
	public Skeleton3D Skeleton { get; private set; }
	
	public CharacterSceneObject() : base()
	{
		BoneObjects = new Dictionary<string, BoneSceneObject>();
		ObjectType = "Character";
		// Characters should have zero pivot offset (models are typically already centered)
		PivotOffset = Vector3.Zero;
	}
	
	/// <summary>
	/// Sets up the character from a loaded GLB scene
	/// Creates BoneSceneObjects for each bone in the skeleton
	/// </summary>
	public void SetupFromGlb(Node3D glbRoot)
	{
		if (glbRoot == null)
		{
			GD.PrintErr("Cannot setup character - GLB root is null");
			return;
		}
		
		// Find the skeleton in the GLB hierarchy
		Skeleton = FindSkeleton(glbRoot);
		
		if (Skeleton == null)
		{
			GD.PrintErr("No skeleton found in GLB file");
			return;
		}
		
		GD.Print($"Found skeleton with {Skeleton.GetBoneCount()} bones");
		
		// Try to find a skinned mesh (some models like Steve have individual meshes per bone instead)
		SkinnedMesh = FindSkinnedMesh(glbRoot);
		
		if (SkinnedMesh == null)
		{
			GD.Print("No skinned mesh found - character likely uses separate meshes per bone");
		}
		else
		{
			GD.Print($"Found skinned mesh: {SkinnedMesh.Name}");
		}
		
		// Add the entire GLB hierarchy to our Visual node
		// Use AddChild instead of Reparent since the glbRoot may not be in the scene tree yet
		Visual.AddChild(glbRoot);
		
		// Create BoneSceneObjects for each bone
		CreateBoneHierarchy();
		
		GD.Print($"Character setup complete with {BoneObjects.Count} bone objects");
	}
	
	/// <summary>
	/// Recursively searches for a Skeleton3D node
	/// </summary>
	private Skeleton3D FindSkeleton(Node node)
	{
		if (node is Skeleton3D skeleton)
		{
			return skeleton;
		}
		
		foreach (var child in node.GetChildren())
		{
			var found = FindSkeleton(child);
			if (found != null)
				return found;
		}
		
		return null;
	}
	
	/// <summary>
	/// Recursively searches for a MeshInstance3D with a Skin (skinned mesh)
	/// </summary>
	private MeshInstance3D FindSkinnedMesh(Node node)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Skin != null)
		{
			return meshInstance;
		}
		
		foreach (var child in node.GetChildren())
		{
			var found = FindSkinnedMesh(child);
			if (found != null)
				return found;
		}
		
		return null;
	}
	
	/// <summary>
	/// Creates a hierarchy of BoneSceneObjects that mirror the skeleton structure
	/// </summary>
	private void CreateBoneHierarchy()
	{
		if (Skeleton == null)
			return;
		
		var boneCount = Skeleton.GetBoneCount();
		
		// First pass: Create all bone objects
		for (int i = 0; i < boneCount; i++)
		{
			var boneName = Skeleton.GetBoneName(i);
			var boneObject = new BoneSceneObject(Skeleton, i);
			boneObject.Name = boneName;
			boneObject.ObjectType = "Bone";
			
			BoneObjects[boneName] = boneObject;
		}
		
		// Second pass: Build hierarchy based on bone parents
		for (int i = 0; i < boneCount; i++)
		{
			var boneName = Skeleton.GetBoneName(i);
			var boneObject = BoneObjects[boneName];
			
			var parentIdx = Skeleton.GetBoneParent(i);
			
			if (parentIdx >= 0)
			{
				// This bone has a parent bone - parent it to that bone's SceneObject
				var parentBoneName = Skeleton.GetBoneName(parentIdx);
				var parentBoneObject = BoneObjects[parentBoneName];
				parentBoneObject.AddChild(boneObject);
			}
			else
			{
				// This is a root bone - add directly to this character object
				AddChild(boneObject);
			}
			
			// Initialize the bone object's transform from the skeleton
			boneObject.UpdateFromSkeleton();
		}
	}
	
	/// <summary>
	/// Updates all bone transforms from the skeleton
	/// </summary>
	public void UpdateBonesFromSkeleton()
	{
		foreach (var boneObject in BoneObjects.Values)
		{
			boneObject.UpdateFromSkeleton();
		}
	}
	
	/// <summary>
	/// Updates the skeleton from the bone scene objects
	/// This is called when bones are manipulated in the editor
	/// </summary>
	public void UpdateSkeletonFromBones()
	{
		foreach (var boneObject in BoneObjects.Values)
		{
			boneObject.UpdateSkeleton();
		}
	}
}

/// <summary>
/// Represents a single bone in a character's skeleton as a SceneObject
/// </summary>
public partial class BoneSceneObject : SceneObject
{
	private Skeleton3D _skeleton;
	private int _boneIdx;
	
	// Target position and rotation for bones - shows as zero in editor but keeps internal values
	private Vector3 _targetPosition = Vector3.Zero;
	private Vector3 _targetRotation = Vector3.Zero;
	
	// Store the base pose position and rotation (from bone rest pose)
	private Vector3 _basePosePosition = Vector3.Zero;
	private Vector3 _basePoseRotation = Vector3.Zero;
	
	// Store the internal "real" position and rotation
	private Vector3 _internalPosition = Vector3.Zero;
	private Vector3 _internalRotation = Vector3.Zero;
	
	public int BoneIndex => _boneIdx;
	public Skeleton3D Skeleton => _skeleton;
	
	/// <summary>
	/// Override Position to show TargetPosition (offset from base pose) in the editor
	/// while maintaining internal position for the skeleton
	/// </summary>
	public new Vector3 Position
	{
		get => _targetPosition;
		set
		{
			_targetPosition = value;
			_internalPosition = _basePosePosition + _targetPosition;
			base.Position = _internalPosition;
		}
	}
	
	/// <summary>
	/// Override Rotation to show TargetRotation (offset from base pose) in the editor
	/// while maintaining internal rotation for the skeleton
	/// </summary>
	public new Vector3 Rotation
	{
		get => _targetRotation;
		set
		{
			_targetRotation = value;
			_internalRotation = _basePoseRotation + _targetRotation;
			base.Rotation = _internalRotation;
		}
	}
	
	/// <summary>
	/// The target position that appears as the "zero" position in the editor
	/// </summary>
	public Vector3 TargetPosition
	{
		get => _targetPosition;
		set
		{
			_targetPosition = value;
			_internalPosition = _basePosePosition + _targetPosition;
			base.Position = _internalPosition;
		}
	}
	
	/// <summary>
	/// The target rotation that appears as the "zero" rotation in the editor
	/// </summary>
	public Vector3 TargetRotation
	{
		get => _targetRotation;
		set
		{
			_targetRotation = value;
			_internalRotation = _basePoseRotation + _targetRotation;
			base.Rotation = _internalRotation;
		}
	}
	
	public BoneSceneObject(Skeleton3D skeleton, int boneIdx) : base()
	{
		_skeleton = skeleton;
		_boneIdx = boneIdx;
		ObjectType = "Bone";
		
		// Bones are selectable but don't need the visual offset
		PivotOffset = Vector3.Zero;
		
		// Create a small visual representation for picking
		CreateBoneVisual();
	}
	
	/// <summary>
	/// Creates a small sphere visual for picking this bone
	/// </summary>
	private void CreateBoneVisual()
	{
		// Create a small sphere mesh for picking
		var meshInstance = new MeshInstance3D();
		var sphereMesh = new SphereMesh();
		sphereMesh.RadialSegments = 8;
		sphereMesh.Rings = 4;
		sphereMesh.Radius = 0.05f; // Small 5cm radius sphere
		sphereMesh.Height = 0.1f;
		
		// Create a simple material
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.5f, 0.5f, 1.0f, 0.3f); // Light blue semi-transparent
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.NoDepthTest = true; // Disable depth test so bone spheres are always visible
		
		// Set material on the mesh surface
		sphereMesh.Material = material;
		meshInstance.Mesh = sphereMesh;
		
		// Set cull layer to 2 (picking layer)
		meshInstance.Layers = 2;
		
		// Add to visual node for picking
		AddVisualInstance(meshInstance);
	}
	
	/// <summary>
	/// Updates this SceneObject's transform from the skeleton bone
	/// </summary>
	public void UpdateFromSkeleton()
	{
		if (_skeleton == null || _boneIdx < 0)
			return;
		
		// Get the bone's rest pose
		var boneRest = _skeleton.GetBoneRest(_boneIdx);
		
		// Store the base pose position and rotation
		_basePosePosition = boneRest.Origin;
		_basePoseRotation = boneRest.Basis.GetEuler();
		
		// Set the internal transform to match the bone's rest position
		_internalPosition = _basePosePosition;
		_internalRotation = _basePoseRotation;
		base.Position = _internalPosition;
		base.Rotation = _internalRotation;
		base.Scale = boneRest.Basis.Scale;
		
		// Initialize target position and rotation to zero (relative to base pose)
		_targetPosition = Vector3.Zero;
		_targetRotation = Vector3.Zero;
	}
	
	/// <summary>
	/// Updates the skeleton bone from this SceneObject's transform
	/// Called when the bone is moved in the editor
	/// </summary>
	public void UpdateSkeleton()
	{
		if (_skeleton == null || _boneIdx < 0)
			return;
		
		// During gizmo editing, read directly from base.Position and base.Rotation
		// to get real-time updates. Otherwise, use the internal values.
		Vector3 posToUse = SelectionManager.Instance?.IsGizmoEditing == true ? base.Position : _internalPosition;
		Vector3 rotToUse = SelectionManager.Instance?.IsGizmoEditing == true ? base.Rotation : _internalRotation;
		
		// Update the bone pose in the skeleton
		_skeleton.SetBonePosePosition(_boneIdx, posToUse);
		// Convert Euler angles to Quaternion for the skeleton
		var quat = Basis.FromEuler(rotToUse).GetRotationQuaternion();
		_skeleton.SetBonePoseRotation(_boneIdx, quat);
		_skeleton.SetBonePoseScale(_boneIdx, base.Scale);
	}
	
	/// <summary>
	/// Called when the gizmo transform ends - updates target position/rotation
	/// </summary>
	public void OnGizmoTransformEnd()
	{
		// Update target position and rotation based on the internal values
		// The gizmo modifies base.Position and base.Rotation, so read from there
		_internalPosition = base.Position;
		_internalRotation = base.Rotation;
		
		// Calculate new target values relative to base pose
		_targetPosition = _internalPosition - _basePosePosition;
		_targetRotation = _internalRotation - _basePoseRotation;
	}
	
	/// <summary>
	/// Called when this bone is transformed
	/// </summary>
	public override void _Process(double delta)
	{
		base._Process(delta);
		
		// Continuously update skeleton from bone transforms during editing
		// This allows real-time manipulation of the character
		UpdateSkeleton();
	}
}
