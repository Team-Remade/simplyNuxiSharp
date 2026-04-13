using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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
	public Dictionary<string, BoneSceneObject> BoneObjects { get; }
	
	// The skeleton that manages the bones
	public Skeleton3D Skeleton { get; private set; }
	
	public CharacterSceneObject()
	{
		BoneObjects = new Dictionary<string, BoneSceneObject>();
		ObjectType = "Character";
		// Characters should have zero pivot offset (models are typically already centered)
		PivotOffset = Vector3.Zero;
	}
	
	/// <summary>
	/// The character name (e.g., "Steve", "Alex") used to look up the character's GLB file.
	/// This is stored separately from ObjectType which is always "Character".
	/// </summary>
	public string CharacterName { get; set; } = "";

	/// <summary>
	/// Bend style for this character model (Realistic, Blocky, or ProjectDefault).
	/// When set to ProjectDefault, uses the project-level bend style setting.
	/// </summary>
	public BendStyle ModelBendStyle { get; set; } = BendStyle.ProjectDefault;
	
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
		
		// Try to find a skinned mesh (some models like Steve have individual meshes per bone instead)
		SkinnedMesh = FindSkinnedMesh(glbRoot);
		
		// Add the entire GLB hierarchy to our Visual node
		// Use AddChild instead of Reparent since the glbRoot may not be in the scene tree yet
		Visual.AddChild(glbRoot);
		
		// Create BoneSceneObjects for each bone
		CreateBoneHierarchy();
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
		if (node is MeshInstance3D { Skin: not null } meshInstance)
		{
			return meshInstance;
		}

		return node.GetChildren().Select(FindSkinnedMesh).FirstOrDefault(found => found != null);
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
            var boneObject = new BoneSceneObject(Skeleton, i)
            {
                Name = boneName,
                ObjectType = "Bone"
            };

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

	/// <summary>
	/// Resets all bones in the skeleton to their rest pose.
	/// Called when loading a character from a saved project to ensure
	/// the model appears in its default T-pose rather than a deformed pose.
	/// </summary>
	public void ResetToRestPose()
	{
		if (Skeleton == null) return;

		for (int i = 0; i < Skeleton.GetBoneCount(); i++)
		{
			// Get the rest pose for this bone
			var rest = Skeleton.GetBoneRest(i);

			// Reset pose to rest position/rotation/scale
			Skeleton.SetBonePosePosition(i, rest.Origin);
			Skeleton.SetBonePoseRotation(i, rest.Basis.GetRotationQuaternion());
			Skeleton.SetBonePoseScale(i, rest.Basis.Scale);
		}
	}
}

/// <summary>
/// Holds all data needed to regenerate a single shape mesh on a bone.
/// Stored so that when the bend angle changes the mesh can be rebuilt.
/// </summary>
public class BoneShapeData
{
	public string PartName;
	public int ShapeIndex;
	public MiShape Shape;
	public MiModel Model;
	public ImageTexture Texture;
	public Vector3 AccumulatedScale;
	public BendStyle ModelBendStyle;
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
	
	// Alpha override for bones that control a single mesh
	private float _alphaOverride = 1.0f;
	public float AlphaOverride
	{
		get => _alphaOverride;
		set
		{
			_alphaOverride = value;
			ApplyAlphaToControlledMesh();
		}
	}

	// ── Bend data ─────────────────────────────────────────────────────────────

	/// <summary>
	/// The bend parameters parsed from the MiPart's bend JSON (null if no bend).
	/// The <see cref="BendParams.Angle"/> field is the *current* editable angle.
	/// </summary>
	public BendParams? BendParameters { get; private set; }

	/// <summary>
	/// The lock_bend value from the MiPart JSON.
	/// When > 0 children should inherit the parent's bent-half transform offset.
	/// </summary>
	public float LockBend { get; private set; }

	/// <summary>Shape data list used to regenerate meshes when bend angle changes.</summary>
	private readonly List<BoneShapeData> _shapeDataList = new();

	/// <summary>
	/// Sets the bend parameters for this bone.
	/// Called by the loader after the bone is created.
	/// </summary>
	public void SetBendParameters(BendParams? bendParams, float lockBend)
	{
		BendParameters = bendParams;
		LockBend = lockBend;
	}

	/// <summary>
	/// Registers shape data so meshes can be regenerated when the bend angle changes.
	/// </summary>
	public void RegisterShapeData(BoneShapeData data)
	{
		_shapeDataList.Add(data);
	}

	/// <summary>
	/// Updates the bend angle and regenerates all shape meshes.
	/// </summary>
	public void SetBendAngle(Vector3 newAngle)
	{
		if (!BendParameters.HasValue) return;

		var bp = BendParameters.Value;
		// Clamp to direction limits
		newAngle.X = Math.Clamp(newAngle.X, bp.DirectionMin.X, bp.DirectionMax.X);
		newAngle.Y = Math.Clamp(newAngle.Y, bp.DirectionMin.Y, bp.DirectionMax.Y);
		newAngle.Z = Math.Clamp(newAngle.Z, bp.DirectionMin.Z, bp.DirectionMax.Z);

		bp.Angle = newAngle;
		BendParameters = bp;

		RegenerateMeshes();
	}

	/// <summary>
	/// Rebuilds all mesh instances for this bone using the current bend angle.
	/// Removes old meshes from the Visual node and creates new ones.
	/// Also updates children's visual positions to reflect the new bend.
	/// Also triggers regeneration on child bones whose InheritBend is true,
	/// so their meshes update when the parent angle changes.
	///
	/// NOTE: Even when this bone has no shapes (empty container part), propagation
	/// to InheritBend children still runs — a shapeless parent can still drive
	/// child bends via inherit_bend (e.g. the mouth's OpenedMouth part).
	/// </summary>
	public void RegenerateMeshes()
	{
		// Only rebuild this bone's own meshes if it has any shapes registered.
		if (_shapeDataList.Count > 0)
		{
			// Remove existing mesh instances from Visual
			var toRemove = new List<Node>();
			foreach (var child in Visual.GetChildren())
			{
				if (child is MeshInstance3D)
					toRemove.Add(child);
			}
			foreach (var node in toRemove)
			{
				Visual.RemoveChild(node);
				node.QueueFree();
			}

			// Build effective bend params: replace Angle with the inherited-compounded angle.
			BendParams? effectiveBendParams = null;
			if (BendParameters.HasValue)
			{
				var bp = BendParameters.Value;
				bp.Angle = GetEffectiveBendAngle();
				effectiveBendParams = bp;
			}

			// Recreate meshes using the loader
			var loader = new MineImatorLoader();
			foreach (var sd in _shapeDataList)
			{
				var meshInstance = loader.CreateShapeMeshPublic(
					sd.PartName, sd.ShapeIndex, sd.Shape, sd.Model,
					sd.Texture, sd.AccumulatedScale, effectiveBendParams, sd.ModelBendStyle);
				if (meshInstance != null)
				{
					AddVisualInstance(meshInstance);
					meshInstance.Name = $"{sd.PartName}_Shape{sd.ShapeIndex}";
				}
			}
		}

		// Update ALL descendants' visual positions so they track the new bend.
		// This includes direct children and any nested objects parented to this bone.
		foreach (var descendant in GetAllDescendants())
		{
			descendant.UpdateVisualPosition();
		}

		// Propagate to child BoneSceneObjects that inherit our bend angle,
		// so their meshes also update when our angle changes.
		// This runs even if this bone has no shapes (shapeless container parts
		// like OpenedMouth still drive their children's effective bend angle).
		foreach (var child in GetChildrenObjects())
		{
			if (child is BoneSceneObject childBone &&
				childBone.BendParameters.HasValue &&
				childBone.BendParameters.Value.InheritBend)
			{
				childBone.RegenerateMeshes();
			}
		}
	}

	/// <summary>
	/// Returns the effective bend angle for this part, adding the parent part's angle
	/// when InheritBend is true. Matches GML el_update_part.gml lines 122-123:
	///   if (parent.BEND &amp;&amp; value[BEND] &amp;&amp; value[INHERIT_BEND])
	///       bend_default_angle += parent.bend_default_angle
	/// </summary>
	private Vector3 GetEffectiveBendAngle()
	{
		if (!BendParameters.HasValue) return Vector3.Zero;
		var angle = BendParameters.Value.Angle;
		if (BendParameters.Value.InheritBend && GetParent() is BoneSceneObject parentBone && parentBone.BendParameters.HasValue)
			angle += parentBone.GetEffectiveBendAngle();
		return angle;
	}

	/// <summary>
	/// Returns the world-space transform that represents the "bent half" pivot
	/// for children that have lock_bend enabled.
	/// This is the transform applied at the bend point (weight = LockBend).
	/// </summary>
	/// <param name="shapePosition">The locked child's position in the parent's space (Godot units).
	/// This is used to correctly calculate the bend pivot so the child's position
	/// follows the end of the bend.</param>
	public Transform3D GetBentHalfTransform(Vector3 shapePosition)
	{
		if (!BendParameters.HasValue || LockBend <= 0f)
			return Transform3D.Identity;

		var b = BendParameters.Value;
		var effectiveAngle = GetEffectiveBendAngle();
		var bendVec = BendHelper.GetBendVector(effectiveAngle, LockBend);
		return BendHelper.GetBendMatrix(b, bendVec, shapePosition);
	}

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
	
	public BoneSceneObject(Skeleton3D skeleton, int boneIdx)
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
        var sphereMesh = new SphereMesh
        {
            RadialSegments = 8,
            Rings = 4,
            Radius = 0.05f, // Small 5cm radius sphere
            Height = 0.1f
        };

        // Create a simple material
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.5f, 0.5f, 1.0f, 0.3f), // Light blue semi-transparent
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // Use render priority to ensure bones render in front of characters
            // This works with depth pre-pass: higher priority renders after (on top)
            RenderPriority = (int)Material.RenderPriorityMax,
            NoDepthTest = true // Also disable depth test for extra visibility
        };

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
		
		// Store the base pose position
		_basePosePosition = boneRest.Origin;
		Scale = boneRest.Basis.Scale;
		
		// Extract the base pose rotation from the rest pose basis
		_basePoseRotation = boneRest.Basis.GetEuler();
		
		// Set the internal transform to match the bone's rest position
		_internalPosition = _basePosePosition;
		_internalRotation = _basePoseRotation;
		base.Position = _internalPosition;
		base.Rotation = _internalRotation;
        
		// Initialize target position and rotation to zero (relative to base pose)
		_targetPosition = Vector3.Zero;
		_targetRotation = Vector3.Zero;
	}

	/// <summary>
	/// Sets the base pose rotation directly to preserve original euler values.
	/// Call this after UpdateFromSkeleton() when loading from MineImator.
	/// </summary>
	public void SetBasePoseRotation(Vector3 rotationEuler)
	{
		_basePoseRotation = rotationEuler;
		_internalRotation = _basePoseRotation;
		base.Rotation = _internalRotation;
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
	
	/// <summary>
	/// Gets the mesh controlled by this bone if it controls a single mesh
	/// Returns null if the bone controls multiple meshes (skinned) or no meshes
	/// </summary>
	public MeshInstance3D GetControlledMesh()
	{
		// Check if this bone has a direct mesh attached to it (individual mesh per bone)
		var meshInstances = GetMeshInstancesRecursively(Visual);
		
		// If this bone has exactly one mesh in its Visual hierarchy, it controls that mesh
		if (meshInstances.Count == 1)
		{
			return meshInstances[0];
		}
		
		// Check if this is part of a skinned mesh setup
		// In that case, the bone doesn't control a single mesh
		var characterParent = GetParentCharacter();
		if (characterParent?.SkinnedMesh != null)
		{
			// This is a skinned mesh - bone doesn't control a single mesh
		}
		
		return null;
	}
	
	/// <summary>
	/// Checks if this bone controls a single mesh (not part of a skinned mesh)
	/// </summary>
	public bool ControlsSingleMesh()
	{
		return GetControlledMesh() != null;
	}
	
	/// <summary>
	/// Applies the alpha override to the controlled mesh if applicable
	/// </summary>
	private void ApplyAlphaToControlledMesh()
	{
		var controlledMesh = GetControlledMesh();
		if (controlledMesh?.Mesh == null)
			return;
		
		// Apply alpha to all surfaces of the controlled mesh
		for (int i = 0; i < controlledMesh.Mesh.GetSurfaceCount(); i++)
		{
			var material = controlledMesh.Mesh.SurfaceGetMaterial(i);
			if (material is StandardMaterial3D stdMat)
			{
				var color = stdMat.AlbedoColor;
				color.A = _alphaOverride;
				stdMat.AlbedoColor = color;
			}
		}
	}
	
	/// <summary>
	/// Gets the parent CharacterSceneObject if this bone is part of a character
	/// </summary>
	private CharacterSceneObject GetParentCharacter()
	{
		var current = GetParent();
		while (current != null)
		{
			if (current is CharacterSceneObject characterObj)
			{
				return characterObj;
			}
			current = current.GetParent();
		}
		return null;
	}
}
