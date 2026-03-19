using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Handles loading and parsing Mine Imator model files (.mimodel)
/// Treats parts as bones and shapes as meshes attached to those bones
/// </summary>
public class MineImatorLoader
{
	private static MineImatorLoader _instance;
	public static MineImatorLoader Instance => _instance ??= new MineImatorLoader();
	
	// Cache of loaded models by path
	private readonly Dictionary<string, MiModel> _modelCache = new();
	
	/// <summary>
	/// Loads a Mine Imator model from a .mimodel file
	/// </summary>
	/// <param name="modelPath">Path to the .mimodel file</param>
	/// <returns>The loaded MiModel, or null if loading failed</returns>
	public MiModel LoadModel(string modelPath)
	{
		if (_modelCache.TryGetValue(modelPath, out var cachedModel))
		{
			return cachedModel;
		}
		
		try
		{
			if (!File.Exists(modelPath))
			{
				GD.PrintErr($"Mine Imator model file not found: {modelPath}");
				return null;
			}
			
			var jsonText = File.ReadAllText(modelPath);
			var model = JsonSerializer.Deserialize<MiModel>(jsonText, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				MaxDepth = 256 // Increase depth limit for deeply nested model structures
			});
			
			if (model == null)
			{
				GD.PrintErr($"Failed to deserialize Mine Imator model: {modelPath}");
				return null;
			}
			
			// Store the directory path for resolving texture paths
			model.DirectoryPath = Path.GetDirectoryName(modelPath);
			model.FullPath = modelPath;
			
			_modelCache[modelPath] = model;
			
			return model;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error loading Mine Imator model '{modelPath}': {ex.Message}");
			return null;
		}
	}
	
	/// <summary>
	/// Creates a CharacterSceneObject with bones from a Mine Imator model
	/// Each bone has its shapes as child MeshInstance3D nodes
	/// </summary>
	/// <param name="model">The loaded MiModel</param>
	/// <returns>A CharacterSceneObject with skeleton and bones</returns>
	public CharacterSceneObject CreateCharacterFromModel(MiModel model)
	{
		if (model == null || model.Parts == null || model.Parts.Count == 0)
		{
			GD.PrintErr("Cannot create character from null or empty model");
			return null;
		}
		
		// Load the texture
		ImageTexture texture = null;
		if (!string.IsNullOrEmpty(model.Texture))
		{
			texture = LoadModelTexture(model);
		}
		
		// Create skeleton
		var skeleton = new Skeleton3D();
		skeleton.Name = "Skeleton";
		
		// Flatten parts and create bones (also tracking accumulated parent scale)
		var boneDataList = new List<(MiPart part, int boneIdx, int parentIdx, Vector3 accumulatedParentScale)>();
		FlattenPartsForBones(model.Parts, -1, Vector3.One, boneDataList);
		
		// Create all bones first
		foreach (var (part, boneIdx, parentIdx, _) in boneDataList)
		{
			CreateBoneFromPart(skeleton, part, boneIdx, parentIdx);
		}
		
		// Create CharacterSceneObject
		var character = new CharacterSceneObject();
		character.Name = model.Name ?? "MineImatorModel";
		character.ObjectType = "MineImator";
		
		// Add skeleton to visual
		character.Visual.AddChild(skeleton);
		
		// Create BoneSceneObjects for each bone first
		CreateBoneSceneObjects(character, skeleton);
		
		// Now add meshes to the BoneSceneObjects
		foreach (var (part, boneIdx, parentIdx, accumulatedParentScale) in boneDataList)
		{
			if (part.Shapes != null && part.Shapes.Count > 0)
			{
				string boneName = skeleton.GetBoneName(boneIdx);
				if (character.BoneObjects.TryGetValue(boneName, out var boneObject))
				{
					// Compute this part's own scale
					Vector3 partScale = Vector3.One;
					if (part.Scale != null && part.Scale.Length >= 3)
					{
						partScale = new Vector3(part.Scale[0], part.Scale[1], part.Scale[2]);
					}
					// Full accumulated scale for shapes = parent accumulated scale * this part's scale
					Vector3 accumulatedScale = accumulatedParentScale * partScale;
					
					// Parse bend parameters from this part (if any)
					// Pass the part's own scale so offset/size are scaled correctly
					BendParams? bendParams = BendHelper.ParseBend(part.Bend, part.Scale);
					
					int shapeIndex = 0;
					foreach (var shape in part.Shapes)
					{
						var meshInstance = CreateShapeMesh(part.Name, shapeIndex, shape, model, texture, accumulatedScale, bendParams);
						if (meshInstance != null)
						{
							// Add the mesh as a visual child of the BoneSceneObject
							boneObject.AddVisualInstance(meshInstance);
							meshInstance.Name = $"{part.Name}_Shape{shapeIndex}";
						}
						shapeIndex++;
					}
				}
			}
		}
		
		return character;
	}
	
	/// <summary>
	/// Flattens the part hierarchy for bone creation, tracking accumulated parent scale
	/// </summary>
	private void FlattenPartsForBones(List<MiPart> parts, int parentIdx, Vector3 accumulatedParentScale,
		List<(MiPart part, int boneIdx, int parentIdx, Vector3 accumulatedParentScale)> boneDataList)
	{
		if (parts == null) return;
		
		foreach (var part in parts)
		{
			int currentIdx = boneDataList.Count;
			boneDataList.Add((part, currentIdx, parentIdx, accumulatedParentScale));
			
			if (part.Parts != null && part.Parts.Count > 0)
			{
				// Compute this part's scale to pass down as accumulated scale to children
				Vector3 partScale = Vector3.One;
				if (part.Scale != null && part.Scale.Length >= 3)
				{
					partScale = new Vector3(part.Scale[0], part.Scale[1], part.Scale[2]);
				}
				Vector3 childAccumulatedScale = accumulatedParentScale * partScale;
				FlattenPartsForBones(part.Parts, currentIdx, childAccumulatedScale, boneDataList);
			}
		}
	}
	
	/// <summary>
	/// Creates a bone from a part
	/// </summary>
	private void CreateBoneFromPart(Skeleton3D skeleton, MiPart part, int boneIdx, int parentIdx)
	{
		string boneName = part.Name ?? $"Bone_{boneIdx}";
		
		// Convert position from Mine Imator to Godot
		// All parts (including root) use their actual position data
		Vector3 position = Vector3.Zero;
		if (part.Position != null && part.Position.Length >= 3)
		{
			// Convert from pixels to blocks (divide by 16)
			// Mine Imator: Y-up, Z-forward; Godot: Y-up, Z-forward
			position = new Vector3(
				part.Position[0] / 16.0f,
				part.Position[1] / 16.0f,
				part.Position[2] / 16.0f
			);
		}
		
		// Convert rotation from degrees to radians
		Vector3 rotation = Vector3.Zero;
		if (part.Rotation != null && part.Rotation.Length >= 3)
		{
			rotation = new Vector3(
				Mathf.DegToRad(part.Rotation[0]),
				Mathf.DegToRad(part.Rotation[1]),
				Mathf.DegToRad(-part.Rotation[2]) // Inverted
			);
		}
		
		// Get scale
		Vector3 scale = Vector3.One;
		if (part.Scale != null && part.Scale.Length >= 3)
		{
			scale = new Vector3(part.Scale[0], part.Scale[1], part.Scale[2]);
		}
		
		// Add bone
		int addedIdx = skeleton.AddBone(boneName);
		
		// Set parent
		if (parentIdx >= 0)
		{
			skeleton.SetBoneParent(addedIdx, parentIdx);
		}
		
		// Create rest pose transform
		// MineImator uses: matrix_create(position, rotation, scale) where rotation is applied before position
		// In Godot, we need to apply rotation first, then translation (reverse order from current)
		// Using XYZ rotation order to match MineImator
		var restTransform = Transform3D.Identity
			.Rotated(Vector3.Right, rotation.X)
			.Rotated(Vector3.Up, rotation.Y)
			.Rotated(Vector3.Forward, rotation.Z)
			.Translated(position);

		skeleton.SetBoneRest(addedIdx, restTransform);
		skeleton.SetBonePosePosition(addedIdx, position);
		skeleton.SetBonePoseRotation(addedIdx, Quaternion.FromEuler(rotation));
		skeleton.SetBonePoseScale(addedIdx, Vector3.One); // Scale is handled at shape level, not bone level
	}
	
	/// <summary>
	/// Creates BoneSceneObjects for each bone in the skeleton
	/// </summary>
	private void CreateBoneSceneObjects(CharacterSceneObject character, Skeleton3D skeleton)
	{
		int boneCount = skeleton.GetBoneCount();
		
		// First pass: Create all bone objects
		for (int i = 0; i < boneCount; i++)
		{
			var boneName = skeleton.GetBoneName(i);
            var boneObject = new BoneSceneObject(skeleton, i)
            {
                Name = boneName,
                ObjectType = "Bone"
            };

            character.BoneObjects[boneName] = boneObject;
		}
		
		// Second pass: Build hierarchy
		for (int i = 0; i < boneCount; i++)
		{
			var boneName = skeleton.GetBoneName(i);
			var boneObject = character.BoneObjects[boneName];
			
			int parentIdx = skeleton.GetBoneParent(i);
			
			if (parentIdx >= 0)
			{
				var parentName = skeleton.GetBoneName(parentIdx);
				var parentObject = character.BoneObjects[parentName];
				parentObject.AddChild(boneObject);
			}
			else
			{
				// Root bone
				character.AddChild(boneObject);
			}
			
			boneObject.UpdateFromSkeleton();
		}
	}
	
	/// <summary>
	/// Creates a MeshInstance3D for a single shape
	/// </summary>
	/// <param name="accumulatedParentScale">The product of all ancestor part scales up the hierarchy</param>
	/// <param name="bendParams">Bend parameters from the parent part (null if no bending)</param>
	private MeshInstance3D CreateShapeMesh(string partName, int shapeIndex, MiShape shape, MiModel model, ImageTexture texture, Vector3 accumulatedParentScale, BendParams? bendParams = null)
	{
		if (shape == null || shape.From == null || shape.To == null)
		{
			return null;
		}
		
		int texWidth = model.TextureSize?[0] ?? 64;
		int texHeight = model.TextureSize?[1] ?? 64;
		
		// Get UV offset (top-left corner of texture region)
		float uvU = shape.Uv?[0] ?? 0;
		float uvV = shape.Uv?[1] ?? 0;
		
		// Convert coordinates from pixels to blocks (divide by 16)
		// JSON uses Y-up (Y=height, Z=depth), same as Godot
		Vector3 from = new Vector3(
			shape.From[0] / 16.0f,
			shape.From[1] / 16.0f,
			shape.From[2] / 16.0f
		);
		
		Vector3 to = new Vector3(
			shape.To[0] / 16.0f,
			shape.To[1] / 16.0f,
			shape.To[2] / 16.0f
		);
		
		// Calculate size in pixels for UV mapping
		float sizeX = Math.Abs(shape.To[0] - shape.From[0]);
		float sizeY = Math.Abs(shape.To[1] - shape.From[1]);
		float sizeZ = Math.Abs(shape.To[2] - shape.From[2]);
		
		// Apply position
		Vector3 shapePosition = Vector3.Zero;
		if (shape.Position != null && shape.Position.Length >= 3)
		{
			shapePosition = new Vector3(shape.Position[0] / 16, shape.Position[1] / 16, shape.Position[2] / 16);
		}
		
		// Apply shape rotation if present
		// Convert from Mine Imator Y-up to Godot Y-up Euler angles:
		// Mine Imator: X=left/right, Y=up/down, Z=front/back
		// Godot:      X=left/right, Y=up/down,    Z=front/back
		// So: godotX = rotX, godotY = rotY, godotZ = -rotZ   (negate Z to match part rotation conversion)
		Vector3 shapeRotation = Vector3.Zero;
		if (shape.Rotation != null && shape.Rotation.Length >= 3)
		{
			float rotX = Mathf.DegToRad(shape.Rotation[0]);
			float rotY = Mathf.DegToRad(shape.Rotation[1]);
			float rotZ = -Mathf.DegToRad(shape.Rotation[2]); // Negate Z as in part rotation
			shapeRotation = new Vector3(rotX, rotY, rotZ);
		}
		
		// Apply shape scale if present, then multiply by the accumulated parent scale from the hierarchy
		Vector3 shapeScale = Vector3.One;
		if (shape.Scale != null && shape.Scale.Length >= 3)
		{
			shapeScale = new Vector3(shape.Scale[0], shape.Scale[1], shape.Scale[2]);
		}
		// Incorporate the accumulated scale from all ancestor parts
		shapeScale *= accumulatedParentScale;
		
		// Get inflate value and scale it from Minecraft pixels to Godot units (divide by 16)
		float inflate = shape.Inflate / 16.0f;
		
		// Only apply bending if the shape has bend enabled (bend_shape flag)
		BendParams? effectiveBend = (shape.Bend && bendParams.HasValue) ? bendParams : null;
		
		MeshInstance3D meshInstance;
		
		bool planeBent = effectiveBend.HasValue && (
			effectiveBend.Value.Angle.X != 0 || effectiveBend.Value.Angle.Y != 0 || effectiveBend.Value.Angle.Z != 0);
		
		if (shape.Type == "plane")
		{
			if (shape.ThreeD)
			{
				if (planeBent)
				{
					// Bent 3D plane: per-pixel extruded geometry with bend deformation
					meshInstance = CreateBentExtrudedPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
						texture, shape.TextureMirror, shape.Invert, inflate, effectiveBend.Value, shapePosition, shapeRotation);
				}
				else
				{
					// Regular extruded item-like plane with per-pixel hull mesh
					meshInstance = CreateExtrudedPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, texture, shape.TextureMirror, shape.Invert, inflate);
				}
			}
			else if (planeBent)
			{
				// Bent 2D plane: segmented geometry matching Modelbench's algorithm
				meshInstance = CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
					shape.TextureMirror, shape.Invert, inflate, effectiveBend.Value, shapePosition, shapeRotation);
			}
			else
			{
				// Regular 2D plane (no bending)
				meshInstance = CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate);
			}
		}
		else // "block" or default
		{
			meshInstance = CreateBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate, effectiveBend, shapePosition, shapeRotation);
		}
		
		// Apply shape scale to the mesh instance
		// NOTE: When bending is applied, the rotation AND position are baked into the mesh vertices via
		// GetBendMatrix(). The vertices already include T(pivot), so we do NOT set meshInstance.Position
		// in that case. Only set rotation on mesh instance when NOT bent.
		if (meshInstance != null)
		{
			// When bent, position is baked into vertices (via T(pivot) in GetBendMatrix)
			// When not bent, position needs to be set normally
			if (!effectiveBend.HasValue)
			{
				meshInstance.Position = shapePosition;
				meshInstance.Rotation = shapeRotation;
			}
			
			meshInstance.Scale = shapeScale;
		}
		
		// Apply material with texture
		if (meshInstance != null && texture != null)
		{
	           var material = new StandardMaterial3D
	           {
	               AlbedoTexture = texture,
	               TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
	               // Use backface culling by default, frontface culling when inverted
	               CullMode = BaseMaterial3D.CullModeEnum.Back,
	               Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
	               AlphaScissorThreshold = 0.5f
	           };

	           if (meshInstance.Mesh is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
			{
				arrayMesh.SurfaceSetMaterial(0, material);
			}
			
			meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
		}
		
		return meshInstance;
	}
	
	/// <summary>
	/// Creates a block (cube) mesh, optionally with bend deformation.
	/// When bend is provided, the block is split into segments along the bend axis
	/// and each segment is progressively rotated using the Modelbench easing algorithm.
	/// </summary>
	/// <param name="shapePosition">The shape's position in part-local space (Godot units), used for bend pivot calculation</param>
	/// <param name="shapeRotation">The shape's rotation in radians (Godot Y-up Euler angles), applied during bend deformation</param>
	private MeshInstance3D CreateBlockMesh(string partName, int shapeIndex, Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, float sizeZ, int texWidth, int texHeight,
		bool textureMirror, bool invert, float inflate = 0.0f, BendParams? bend = null, Vector3 shapePosition = default, Vector3 shapeRotation = default)
	{
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var uvs = new List<Vector2>();
		var indices = new List<int>();
		
		// Ensure proper ordering
		Vector3 min = new Vector3(
			Math.Min(from.X, to.X),
			Math.Min(from.Y, to.Y),
			Math.Min(from.Z, to.Z)
		);
		Vector3 max = new Vector3(
			Math.Max(from.X, to.X),
			Math.Max(from.Y, to.Y),
			Math.Max(from.Z, to.Z)
		);
		
		// Apply inflate - expand the mesh bounds by the inflate amount in all directions
		if (inflate != 0.0f)
		{
			min = new Vector3(min.X - inflate, min.Y - inflate, min.Z - inflate);
			max = new Vector3(max.X + inflate, max.Y + inflate, max.Z + inflate);
		}
		
		// Convert sizes to normalized UV coordinates (0-1 range)
		// Using the same UV layout as Mine-imator GML project
		float texU = uvU / texWidth;
		float texV = uvV / texHeight;
		
		// Texture sizes normalized (with 1/256 pixel fix for rendering artifacts)
		float texSizeX = sizeX / texWidth;
		float texSizeY = sizeY / texHeight;
		float texSizeZ = sizeZ / texHeight; // Z uses texture height for V coordinate
		
		// Artifact fix: subtract 1/256 pixel to avoid rendering issues
		float texSizeFixX = (sizeX - 1.0f/256.0f) / texWidth;
		float texSizeFixY = (sizeY - 1.0f/256.0f) / texHeight;
		float texSizeFixZ = (sizeZ - 1.0f/256.0f) / texHeight;
		
		// UV coordinates for each face (following Mine-imator layout):
		// South (Front Z+): at UV origin, size (X, Z)
		// East (Right X+): to the right of South, size (Y, Z)
		// West (Left X-): to the left of South, size (Y, Z)
		// North (Back Z-): to the right of East, size (X, Z)
		// Up (Top Y+): above South, size (X, Y)
		// Down (Bottom Y-): to the right of Up, size (X, Y) - flipped vertically
		
		// South face (Front, Z+) - at UV origin, uses X for width, Y for height
		var texSouth1 = new Vector2(texU, texV);
		var texSouth2 = new Vector2(texU + texSizeFixX, texV);
		var texSouth3 = new Vector2(texU + texSizeFixX, texV + texSizeFixY);
		var texSouth4 = new Vector2(texU, texV + texSizeFixY);
		
		// East face (Right, X+) - to the LEFT of South by Z, uses Z for width, Y for height
		var texEast1 = new Vector2(texU - texSizeZ, texV);
		var texEast2 = new Vector2(texU - texSizeZ + texSizeFixZ, texV);
		var texEast3 = new Vector2(texU - texSizeZ + texSizeFixZ, texV + texSizeFixY);
		var texEast4 = new Vector2(texU - texSizeZ, texV + texSizeFixY);
		
		// West face (Left, X-) - to the RIGHT of South by Z, uses Z for width, Y for height
		var texWest1 = new Vector2(texU + texSizeZ, texV);
		var texWest2 = new Vector2(texU + texSizeZ + texSizeFixZ, texV);
		var texWest3 = new Vector2(texU + texSizeZ + texSizeFixZ, texV + texSizeFixY);
		var texWest4 = new Vector2(texU + texSizeZ, texV + texSizeFixY);
		
		// North face (Back, Z-) - to the right of West by X, uses X for width, Y for height
		var texNorth1 = new Vector2(texU + texSizeZ + texSizeX, texV);
		var texNorth2 = new Vector2(texU + texSizeZ + texSizeX + texSizeFixX, texV);
		var texNorth3 = new Vector2(texU + texSizeZ + texSizeX + texSizeFixX, texV + texSizeFixY);
		var texNorth4 = new Vector2(texU + texSizeZ + texSizeX, texV + texSizeFixY);
		
		// Flip East and West face UVs horizontally (swap left/right)
		(texEast1, texEast2) = (texEast2, texEast1);
		(texEast3, texEast4) = (texEast4, texEast3);
		(texWest1, texWest2) = (texWest2, texWest1);
		(texWest3, texWest4) = (texWest4, texWest3);
		
		// Up face (Top, Y+) - above South, uses X for width, min(Y,Z) for height
		float texUpHeight = Math.Min(sizeY, sizeZ);
		float texUpHeightFix = (texUpHeight - 1.0f/256.0f) / texHeight;
		var texUp1 = new Vector2(texU, texV - texUpHeightFix);
		var texUp2 = new Vector2(texU + texSizeFixX, texV - texUpHeightFix);
		var texUp3 = new Vector2(texU + texSizeFixX, texV - texUpHeightFix + texUpHeightFix);
		var texUp4 = new Vector2(texU, texV - texUpHeightFix + texUpHeightFix);
		
		// Down face (Bottom, Y-) - to the right of Up by X, uses X for width, min(Y,Z) for height
		var texDown4 = new Vector2(texU + texSizeX, texV - texUpHeightFix);
		var texDown3 = new Vector2(texU + texSizeX + texSizeFixX, texV - texUpHeightFix);
		var texDown2 = new Vector2(texU + texSizeX + texSizeFixX, texV - texUpHeightFix + texUpHeightFix);
		var texDown1 = new Vector2(texU + texSizeX, texV - texUpHeightFix + texUpHeightFix);
		
		// Apply texture mirror on X if needed
		if (textureMirror)
		{
			// Swap east/west UVs
			(texEast1, texWest1) = (texWest1, texEast1);
			(texEast2, texWest2) = (texWest2, texEast2);
			(texEast3, texWest3) = (texWest3, texEast3);
			(texEast4, texWest4) = (texWest4, texEast4);
			
			// Swap left/right points within each face
			(texEast1, texEast2) = (texEast2, texEast1);
			(texEast3, texEast4) = (texEast4, texEast3);
			(texWest1, texWest2) = (texWest2, texWest1);
			(texWest3, texWest4) = (texWest4, texWest3);
			(texSouth1, texSouth2) = (texSouth2, texSouth1);
			(texSouth3, texSouth4) = (texSouth4, texSouth3);
			(texNorth1, texNorth2) = (texNorth2, texNorth1);
			(texNorth3, texNorth4) = (texNorth4, texNorth3);
			(texUp1, texUp2) = (texUp2, texUp1);
			(texUp3, texUp4) = (texUp4, texUp3);
			(texDown1, texDown2) = (texDown2, texDown1);
			(texDown3, texDown4) = (texDown4, texDown3);
		}
		
		// ── Bend deformation ──────────────────────────────────────────────────────
		// When bend is active, we split the block into segments along the bend axis
		// and progressively rotate each segment using the Modelbench easing algorithm.
		// This matches the GML model_shape_generate_block() logic exactly.
		// isBent is true whenever the part has a bend definition AND the angle is non-zero.
		// (At angle=0 the mesh is flat/undeformed, so no segmentation needed.)
		bool isBent = bend.HasValue && (
			bend.Value.Angle.X != 0 || bend.Value.Angle.Y != 0 || bend.Value.Angle.Z != 0);
		
		if (!isBent)
		{
			// No bending: generate the standard 6-face block
			AddFaceWithUVs(vertices, normals, uvs, indices,
				new Vector3(min.X, min.Y, max.Z), new Vector3(max.X, min.Y, max.Z),
				new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z),
				Vector3.Back, texSouth4, texSouth3, texSouth2, texSouth1, invert);
			
			AddFaceWithUVs(vertices, normals, uvs, indices,
				new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, min.Y, min.Z),
				new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z),
				Vector3.Right, texEast4, texEast3, texEast2, texEast1, invert);
			
			AddFaceWithUVs(vertices, normals, uvs, indices,
				new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, min.Y, max.Z),
				new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z),
				Vector3.Left, texWest4, texWest3, texWest2, texWest1, invert);
			
			AddFaceWithUVs(vertices, normals, uvs, indices,
				new Vector3(max.X, min.Y, min.Z), new Vector3(min.X, min.Y, min.Z),
				new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z),
				Vector3.Forward, texNorth4, texNorth3, texNorth2, texNorth1, invert);
			
			AddFaceWithUVs(vertices, normals, uvs, indices,
				new Vector3(min.X, max.Y, max.Z), new Vector3(max.X, max.Y, max.Z),
				new Vector3(max.X, max.Y, min.Z), new Vector3(min.X, max.Y, min.Z),
				Vector3.Up, texUp4, texUp3, texUp2, texUp1, invert);
			
			AddFaceWithUVs(vertices, normals, uvs, indices,
				new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
				new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z),
				Vector3.Down, texDown4, texDown3, texDown2, texDown1, invert);
		}
		else
		{
			// Bent block: generate segmented geometry matching Modelbench's algorithm
				var b = bend.Value;
			
			// Determine the segment axis based on bend part direction.
			// In the JSON/Godot coordinate system (Y-up):
			//   RIGHT/LEFT  -> X axis (0)
			//   UPPER/LOWER -> Y axis (1) - height is Y in JSON
			//   FRONT/BACK  -> Z axis (2) - depth is Z in JSON
			// Note: Modelbench internally uses Z-up, but the JSON uses Y-up.
			int segAxis; // 0=X, 1=Y, 2=Z
			switch (b.Part)
			{
				case BendPart.Right: case BendPart.Left:   segAxis = 0; break;
				case BendPart.Upper: case BendPart.Lower:  segAxis = 1; break;
				case BendPart.Front: case BendPart.Back:   segAxis = 2; break;
				default: segAxis = 1; break;
			}
			
			// Block extents along each axis
			float x1 = from.X, x2 = to.X;
			float y1 = from.Y, y2 = to.Y;
			float z1 = from.Z, z2 = to.Z;
			
			// Bend region size (in Godot units = pixels/16)
			// Modelbench default is 4 pixels = 0.25 blocks
			float bendSize = b.BendSize / 16.0f;
			float bendOffset = b.BendOffset / 16.0f;
			
			// Number of segments: max(bendSize, 2) to ensure smooth bending
			float detail = Math.Max(b.BendSize, 2);
			float segSize = bendSize / detail;
			
			// Invert angle for LOWER/BACK/LEFT parts (they bend in the opposite direction)
			bool invAngle = (b.Part == BendPart.Lower || b.Part == BendPart.Back || b.Part == BendPart.Left);
			
			// Bend region start/end relative to the shape's local origin.
			// Formula: bendStart = (bend_offset - (shape_pos_along_axis + shape_min_local)) - bendSize/2
			// segAxis 0=X, 1=Y, 2=Z in JSON/Godot coordinate system
			float bendStart, bendEnd;
			switch (segAxis)
			{
				case 0: // X axis (RIGHT/LEFT)
					bendStart = (bendOffset - (shapePosition.X + x1)) - bendSize / 2.0f;
					bendEnd   = (bendOffset - (shapePosition.X + x1)) + bendSize / 2.0f;
					break;
				case 1: // Y axis (UPPER/LOWER - height)
					bendStart = (bendOffset - (shapePosition.Y + y1)) - bendSize / 2.0f;
					bendEnd   = (bendOffset - (shapePosition.Y + y1)) + bendSize / 2.0f;
					break;
				default: // Z axis (FRONT/BACK - depth)
					bendStart = (bendOffset - (shapePosition.Z + z1)) - bendSize / 2.0f;
					bendEnd   = (bendOffset - (shapePosition.Z + z1)) + bendSize / 2.0f;
					break;
			}
			
			// Total size along the segment axis
			float totalSize;
			switch (segAxis)
			{
				case 0: totalSize = x2 - x1; break;
				case 1: totalSize = y2 - y1; break;
				default: totalSize = z2 - z1; break;
			}
			
			// UV texture offsets along the segment axis (for sliding UVs per segment)
			// These match the GML texp1/texp2/texp3 variables
			float texpSide1, texpSide2, texpSide3;
			switch (segAxis)
			{
				case 0: // X axis: South/North/Up/Down slide along X
					texpSide1 = texSouth1.X;
					texpSide2 = texNorth2.X;
					texpSide3 = texDown4.X;
					break;
				case 1: // Y axis: East/West/Up/Down slide along Y
					texpSide1 = texEast2.X;
					texpSide2 = texWest1.X;
					texpSide3 = texUp1.Y;
					break;
				default: // Z axis: East/West/South/North slide along Z
					texpSide1 = texSouth3.Y;
					texpSide2 = texSouth3.Y;
					texpSide3 = texSouth3.Y;
					break;
			}
			
			// Starting face points and normals (the "start cap" of the first segment)
			Vector3 p1, p2, p3, p4;
			Vector3 n1, n2, n3, n4;
			Vector2 texStart1, texStart2, texStart3, texStart4;
			Vector2 texEnd1, texEnd2, texEnd3, texEnd4;
			
			switch (segAxis)
			{
				case 0: // X axis
					p1 = new Vector3(x1, y1, z2);
					p2 = new Vector3(x1, y2, z2);
					p3 = new Vector3(x1, y2, z1);
					p4 = new Vector3(x1, y1, z1);
					n1 = new Vector3(0, 1, 0);
					n2 = new Vector3(0, -1, 0);
					n3 = new Vector3(0, 0, 1);
					n4 = new Vector3(0, 0, -1);
					texStart1 = texWest1; texStart2 = texWest2; texStart3 = texWest3; texStart4 = texWest4;
					texEnd1 = texEast1; texEnd2 = texEast2; texEnd3 = texEast3; texEnd4 = texEast4;
					break;
				case 1: // Y axis
					p1 = new Vector3(x2, y1, z2);
					p2 = new Vector3(x1, y1, z2);
					p3 = new Vector3(x1, y1, z1);
					p4 = new Vector3(x2, y1, z1);
					n1 = new Vector3(1, 0, 0);
					n2 = new Vector3(-1, 0, 0);
					n3 = new Vector3(0, 0, 1);
					n4 = new Vector3(0, 0, -1);
					texStart1 = texNorth1; texStart2 = texNorth2; texStart3 = texNorth3; texStart4 = texNorth4;
					texEnd1 = texSouth1; texEnd2 = texSouth2; texEnd3 = texSouth3; texEnd4 = texSouth4;
					break;
				default: // Z axis
					p1 = new Vector3(x1, y2, z1);
					p2 = new Vector3(x2, y2, z1);
					p3 = new Vector3(x2, y1, z1);
					p4 = new Vector3(x1, y1, z1);
					n1 = new Vector3(1, 0, 0);
					n2 = new Vector3(-1, 0, 0);
					n3 = new Vector3(0, 1, 0);
					n4 = new Vector3(0, -1, 0);
					texStart1 = texDown1; texStart2 = texDown2; texStart3 = texDown3; texStart4 = texDown4;
					texEnd1 = texUp1; texEnd2 = texUp2; texEnd3 = texUp3; texEnd4 = texUp4;
					break;
			}
			
			// Apply initial bend transform to starting points
			float startP;
			if (bendStart > 0)
				startP = 0.0f;
			else if (bendEnd < 0)
				startP = 1.0f;
			else
				startP = 1.0f - bendEnd / bendSize;
			
			if (invAngle) startP = 1.0f - startP;
			
			Vector3 startBendVec = BendHelper.GetBendVector(b.Angle, startP);
			Transform3D startMat = BendHelper.GetBendMatrix(b, startBendVec, shapePosition, shapeRotation);
				
			p1 = startMat * p1;
			p2 = startMat * p2;
			p3 = startMat * p3;
			p4 = startMat * p4;
			n1 = (startMat.Basis * n1).Normalized();
			n2 = (startMat.Basis * n2).Normalized();
			n3 = (startMat.Basis * n3).Normalized();
			n4 = (startMat.Basis * n4).Normalized();
			
			// Iterate over segments
			float segPos = 0.0f;
			while (true)
			{
				// End cap
					if (segPos >= totalSize)
					{
						// Compute cap normal from the current face orientation
						Vector3 capNormal;
						switch (segAxis)
						{
							case 0: capNormal = Vector3.Right; break;
							case 1: capNormal = Vector3.Up; break;
							default: capNormal = Vector3.Back; break;
						}
						switch (segAxis)
						{
							case 0: case 1:
								// p2, p1, p4, p3 form the end cap quad
								AddFaceWithUVs(vertices, normals, uvs, indices,
									p2, p1, p4, p3, capNormal, texEnd1, texEnd2, texEnd3, texEnd4, invert);
								break;
							default:
								// p4, p3, p2, p1 form the end cap quad
								AddFaceWithUVs(vertices, normals, uvs, indices,
									p4, p3, p2, p1, capNormal, texEnd1, texEnd2, texEnd3, texEnd4, invert);
								break;
						}
						break;
					}
					
					// Start cap (only for first segment)
					if (segPos == 0.0f)
					{
						Vector3 startCapNormal;
						switch (segAxis)
						{
							case 0: startCapNormal = Vector3.Left; break;
							case 1: startCapNormal = Vector3.Down; break;
							default: startCapNormal = Vector3.Forward; break;
						}
						// p1, p2, p3, p4 form the start cap quad
						AddFaceWithUVs(vertices, normals, uvs, indices,
							p1, p2, p3, p4, startCapNormal, texStart1, texStart2, texStart3, texStart4, invert);
					}
				
				// Determine segment size
				float curSegSize;
				if (segPos >= bendEnd)
					curSegSize = totalSize - segPos;
				else if (segPos < bendStart)
					curSegSize = Math.Min(totalSize - segPos, bendStart);
				else
				{
					curSegSize = segSize;
					if (segPos == 0.0f)
					{
						float fromCoord;
							// fromCoord is the shape's minimum in part space (shape_local + shape_position)
							switch (segAxis)
							{
								case 0: fromCoord = x1 + shapePosition.X; break;
								case 1: fromCoord = y1 + shapePosition.Y; break;
								default: fromCoord = z1 + shapePosition.Z; break;
							}
							curSegSize -= (fromCoord - bendStart) % segSize;
					}
					curSegSize = Math.Min(totalSize - segPos, curSegSize);
				}
				
				segPos += Math.Max(curSegSize, 0.005f);
				
				// Compute next segment points
				Vector3 np1, np2, np3, np4;
				Vector3 nn1, nn2, nn3, nn4;
				float ntexpSide1, ntexpSide2, ntexpSide3;
				
				switch (segAxis)
				{
					case 0: // X axis
					{
						np1 = new Vector3(x1 + segPos, y1, z2);
						np2 = new Vector3(x1 + segPos, y2, z2);
						np3 = new Vector3(x1 + segPos, y2, z1);
						np4 = new Vector3(x1 + segPos, y1, z1);
						nn1 = new Vector3(0, 1, 0);
						nn2 = new Vector3(0, -1, 0);
						nn3 = new Vector3(0, 0, 1);
						nn4 = new Vector3(0, 0, -1);
						float toff = (segPos / totalSize) * texSizeFixX * (textureMirror ? -1 : 1);
						ntexpSide1 = texSouth1.X + toff;
						ntexpSide2 = texNorth2.X - toff;
						ntexpSide3 = texDown4.X + toff;
						break;
					}
					case 1: // Y axis
					{
						np1 = new Vector3(x2, y1 + segPos, z2);
						np2 = new Vector3(x1, y1 + segPos, z2);
						np3 = new Vector3(x1, y1 + segPos, z1);
						np4 = new Vector3(x2, y1 + segPos, z1);
						nn1 = new Vector3(1, 0, 0);
						nn2 = new Vector3(-1, 0, 0);
						nn3 = new Vector3(0, 0, 1);
						nn4 = new Vector3(0, 0, -1);
						float toff = (segPos / totalSize) * texSizeFixY;
						ntexpSide1 = texEast2.X - toff * (textureMirror ? -1 : 1);
						ntexpSide2 = texWest1.X + toff * (textureMirror ? -1 : 1);
						ntexpSide3 = texUp1.Y + toff;
						break;
					}
					default: // Z axis
					{
						np1 = new Vector3(x1, y2, z1 + segPos);
						np2 = new Vector3(x2, y2, z1 + segPos);
						np3 = new Vector3(x2, y1, z1 + segPos);
						np4 = new Vector3(x1, y1, z1 + segPos);
						nn1 = new Vector3(1, 0, 0);
						nn2 = new Vector3(-1, 0, 0);
						nn3 = new Vector3(0, 1, 0);
						nn4 = new Vector3(0, -1, 0);
						float toff = (segPos / totalSize) * texSizeFixZ;
						ntexpSide1 = texSouth3.Y - toff;
						ntexpSide2 = ntexpSide1;
						ntexpSide3 = ntexpSide1;
						break;
					}
				}
				
				// Compute bend weight for this segment
				float segP;
				if (segPos < bendStart)
					segP = 0.0f;
				else if (segPos >= bendEnd)
					segP = 1.0f;
				else
					segP = (1.0f - (bendEnd - segPos) / bendSize);
				
				if (invAngle) segP = 1.0f - segP;
				
				Vector3 segBendVec = BendHelper.GetBendVector(b.Angle, segP);
				Transform3D segMat = BendHelper.GetBendMatrix(b, segBendVec, shapePosition, shapeRotation);
				
				np1 = segMat * np1;
				np2 = segMat * np2;
				np3 = segMat * np3;
				np4 = segMat * np4;
				nn1 = (segMat.Basis * nn1).Normalized();
				nn2 = (segMat.Basis * nn2).Normalized();
				nn3 = (segMat.Basis * nn3).Normalized();
				nn4 = (segMat.Basis * nn4).Normalized();
				
				// Add surrounding faces for this segment
				switch (segAxis)
				{
					case 0: // X axis
					{
						// South
						var t1 = new Vector2(texpSide1, texSouth1.Y);
						var t2 = new Vector2(ntexpSide1, texSouth1.Y);
						var t3 = new Vector2(ntexpSide1, texSouth3.Y);
						var t4 = new Vector2(texpSide1, texSouth3.Y);
						AddFaceWithUVs(vertices, normals, uvs, indices, p2, np2, np3, p3, n1, nn1, nn1, n1, t1, t2, t3, t4, invert);
						// North
						t1 = new Vector2(ntexpSide2, texNorth1.Y);
						t2 = new Vector2(texpSide2, texNorth1.Y);
						t3 = new Vector2(texpSide2, texNorth3.Y);
						t4 = new Vector2(ntexpSide2, texNorth3.Y);
						AddFaceWithUVs(vertices, normals, uvs, indices, np1, p1, p4, np4, nn2, n2, n2, nn2, t1, t2, t3, t4, invert);
						// Up
						t1 = new Vector2(texpSide1, texUp1.Y);
						t2 = new Vector2(ntexpSide1, texUp1.Y);
						t3 = new Vector2(ntexpSide1, texUp3.Y);
						t4 = new Vector2(texpSide1, texUp3.Y);
						AddFaceWithUVs(vertices, normals, uvs, indices, p1, np1, np2, p2, n3, nn3, nn3, n3, t1, t2, t3, t4, invert);
						// Down
						t1 = new Vector2(texpSide3, texDown1.Y);
						t2 = new Vector2(ntexpSide3, texDown1.Y);
						t3 = new Vector2(ntexpSide3, texDown3.Y);
						t4 = new Vector2(texpSide3, texDown3.Y);
						AddFaceWithUVs(vertices, normals, uvs, indices, p3, np3, np4, p4, n4, nn4, nn4, n4, t1, t2, t3, t4, invert);
						texpSide1 = ntexpSide1; texpSide2 = ntexpSide2; texpSide3 = ntexpSide3;
						break;
					}
					case 1: // Y axis
					{
						// East
						var t1 = new Vector2(ntexpSide1, texEast1.Y);
						var t2 = new Vector2(texpSide1, texEast1.Y);
						var t3 = new Vector2(texpSide1, texEast3.Y);
						var t4 = new Vector2(ntexpSide1, texEast3.Y);
						AddFaceWithUVs(vertices, normals, uvs, indices, np1, p1, p4, np4, nn1, n1, n1, nn1, t1, t2, t3, t4, invert);
						// West
						t1 = new Vector2(texpSide2, texWest1.Y);
						t2 = new Vector2(ntexpSide2, texWest1.Y);
						t3 = new Vector2(ntexpSide2, texWest3.Y);
						t4 = new Vector2(texpSide2, texWest3.Y);
						AddFaceWithUVs(vertices, normals, uvs, indices, p2, np2, np3, p3, n2, nn2, nn2, n2, t1, t2, t3, t4, invert);
						// Up
						t1 = new Vector2(texUp1.X, texpSide3);
						t2 = new Vector2(texUp2.X, texpSide3);
						t3 = new Vector2(texUp2.X, ntexpSide3);
						t4 = new Vector2(texUp1.X, ntexpSide3);
						AddFaceWithUVs(vertices, normals, uvs, indices, p2, p1, np1, np2, n3, nn3, nn3, n3, t1, t2, t3, t4, invert);
						// Down
						t1 = new Vector2(texDown1.X, ntexpSide3);
						t2 = new Vector2(texDown2.X, ntexpSide3);
						t3 = new Vector2(texDown2.X, texpSide3);
						t4 = new Vector2(texDown1.X, texpSide3);
						AddFaceWithUVs(vertices, normals, uvs, indices, np3, np4, p4, p3, n4, nn4, t1, t2, t3, t4, invert);
						texpSide1 = ntexpSide1; texpSide2 = ntexpSide2; texpSide3 = ntexpSide3;
						break;
					}
					default: // Z axis
					{
						// East
						var t1 = new Vector2(texEast1.X, ntexpSide1);
						var t2 = new Vector2(texEast2.X, ntexpSide1);
						var t3 = new Vector2(texEast2.X, texpSide1);
						var t4 = new Vector2(texEast1.X, texpSide1);
						AddFaceWithUVs(vertices, normals, uvs, indices, np2, np3, p3, p2, n1, nn1, t1, t2, t3, t4, invert);
						// West
						t1 = new Vector2(texWest1.X, ntexpSide1);
						t2 = new Vector2(texWest2.X, ntexpSide1);
						t3 = new Vector2(texWest2.X, texpSide1);
						t4 = new Vector2(texWest1.X, texpSide1);
						AddFaceWithUVs(vertices, normals, uvs, indices, np4, np1, p1, p4, n2, nn2, t1, t2, t3, t4, invert);
						// South
						t1 = new Vector2(texSouth1.X, ntexpSide1);
						t2 = new Vector2(texSouth2.X, ntexpSide1);
						t3 = new Vector2(texSouth2.X, texpSide1);
						t4 = new Vector2(texSouth1.X, texpSide1);
						AddFaceWithUVs(vertices, normals, uvs, indices, np1, np2, p2, p1, n3, nn3, t1, t2, t3, t4, invert);
						// North
						t1 = new Vector2(texNorth1.X, ntexpSide1);
						t2 = new Vector2(texNorth2.X, ntexpSide1);
						t3 = new Vector2(texNorth2.X, texpSide1);
						t4 = new Vector2(texNorth1.X, texpSide1);
						AddFaceWithUVs(vertices, normals, uvs, indices, np3, np4, p4, p3, n4, nn4, t1, t2, t3, t4, invert);
						texpSide1 = ntexpSide1;
						break;
					}
				}
				
				p1 = np1; p2 = np2; p3 = np3; p4 = np4;
				n1 = nn1; n2 = nn2; n3 = nn3; n4 = nn4;
			}
		}
		
		// Create the mesh
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

	       var meshInstance = new MeshInstance3D
	       {
	           Mesh = arrayMesh
	       };

	       return meshInstance;
	}
	
	/// <summary>
	/// Creates a bent plane mesh, segmented along the bend axis.
	/// Matches Modelbench's model_shape_generate_plane() algorithm exactly.
	///
	/// Coordinate system notes:
	///   In Godot Y-up (same as the JSON format):
	///     - A plane is flat on Z (thin in Z, extends in X and Y).
	///     - The existing CreatePlaneMesh() creates faces at min.Z and max.Z.
	///   In Modelbench Z-up (internal):
	///     - A plane is flat on Y (depth), extends in X and Z.
	///     - GML segaxis=X for RIGHT/LEFT, segaxis=Z for FRONT/BACK/UPPER/LOWER.
	///     - Modelbench Z (height) maps to Godot Y, so GML segaxis=Z → Godot segAxis=Y (1).
	///
	/// So in Godot:
	///   - segAxis=0 (X) for RIGHT/LEFT
	///   - segAxis=1 (Y) for FRONT/BACK/UPPER/LOWER
	///   - The plane is always flat on Z (z1 ≈ z2 for a true plane, or thin in Z).
	/// </summary>
	/// <param name="shapePosition">Shape position in part-local space (Godot units), used for bend pivot</param>
	/// <param name="shapeRotation">Shape rotation in radians (Godot Y-up Euler angles), applied during bend deformation</param>
	private MeshInstance3D CreateBentPlaneMesh(Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, int texWidth, int texHeight,
		bool textureMirror, bool invert, float inflate, BendParams bend, Vector3 shapePosition, Vector3 shapeRotation = default)
	{
		var vertices = new List<Vector3>();
		var normals  = new List<Vector3>();
		var uvs      = new List<Vector2>();
		var indices  = new List<int>();

		// Ensure proper ordering
		float x1 = Math.Min(from.X, to.X);
		float x2 = Math.Max(from.X, to.X);
		float y1 = Math.Min(from.Y, to.Y);
		float y2 = Math.Max(from.Y, to.Y);
		float z1 = from.Z; // plane is flat on Z – use the Z from 'from' as the plane's Z position

		// Apply inflate (expand X and Y extents; Z is the flat axis so we don't expand it)
		if (inflate != 0.0f)
		{
			x1 -= inflate; x2 += inflate;
			y1 -= inflate; y2 += inflate;
		}

		// ── UV setup ──────────────────────────────────────────────────────────
		// Matches model_shape_generate_plane.gml:
		//   texsize[X] = width in pixels / texture width
		//   texsize[Z] = height in pixels / texture height  (GML Z = Godot Y = height)
		float texU = uvU / texWidth;
		float texV = uvV / texHeight;
		float texSizeX = sizeX / texWidth;
		float texSizeY = sizeY / texHeight; // sizeY = height dimension (Godot Y = Modelbench Z)

		// tex1 = top-left, tex2 = top-right, tex3 = bottom-right, tex4 = bottom-left
		var tex1 = new Vector2(texU,           texV);
		var tex2 = new Vector2(texU + texSizeX, texV);
		var tex3 = new Vector2(texU + texSizeX, texV + texSizeY);
		var tex4 = new Vector2(texU,            texV + texSizeY);

		if (textureMirror)
		{
			(tex1, tex2) = (tex2, tex1);
			(tex3, tex4) = (tex4, tex3);
		}

		// ── Bend parameters ───────────────────────────────────────────────────
		var b = bend;

		// Segment axis in Godot Y-up:
		//   RIGHT/LEFT  → X (0)  [same as blocks]
		//   FRONT/BACK/UPPER/LOWER → Y (1)  [GML uses Z=height, Godot Y=height]
		int segAxis; // 0=X, 1=Y
		switch (b.Part)
		{
			case BendPart.Right: case BendPart.Left:
				segAxis = 0; break;
			default: // Front, Back, Upper, Lower
				segAxis = 1; break;
		}

		float bendSize   = b.BendSize   / 16.0f;
		float bendOffset = b.BendOffset / 16.0f;

		float detail  = Math.Max(b.BendSize, 2);
		float segSize = bendSize / detail;

		bool invAngle = (b.Part == BendPart.Lower || b.Part == BendPart.Back || b.Part == BendPart.Left);

		// Total size along the segment axis
		float totalSize = (segAxis == 0) ? (x2 - x1) : (y2 - y1);

		// Bend region start/end relative to the shape's local origin
		float bendStart, bendEnd;
		if (segAxis == 0)
		{
			bendStart = (bendOffset - (shapePosition.X + x1)) - bendSize / 2.0f;
			bendEnd   = (bendOffset - (shapePosition.X + x1)) + bendSize / 2.0f;
		}
		else // Y axis
		{
			bendStart = (bendOffset - (shapePosition.Y + y1)) - bendSize / 2.0f;
			bendEnd   = (bendOffset - (shapePosition.Y + y1)) + bendSize / 2.0f;
		}

		// ── Starting edge points ──────────────────────────────────────────────
		// The plane is flat on Z (at z1). Normals point along Z (forward/backward).
		// For segAxis=X: left edge (x=x1), two points along Y
		//   GML: p1=(x1,y1,z2), p2=(x1,y1,z1) → Godot: p1=(x1,y2,z1), p2=(x1,y1,z1)
		// For segAxis=Y: bottom edge (y=y1), two points along X
		//   GML: p1=(x1,y1,z1), p2=(x2,y1,z1) → Godot: p1=(x1,y1,z1), p2=(x2,y1,z1)
		Vector3 p1, p2;
		float texp1; // sliding UV coordinate

		if (segAxis == 0)
		{
			// Left edge: p1 = top-left, p2 = bottom-left
			p1 = new Vector3(x1, y2, z1);
			p2 = new Vector3(x1, y1, z1);
			texp1 = tex1.X;
		}
		else
		{
			// Bottom edge: p1 = bottom-left, p2 = bottom-right
			p1 = new Vector3(x1, y1, z1);
			p2 = new Vector3(x2, y1, z1);
			texp1 = tex3.Y; // bottom V (slides upward as Y increases)
		}

		// ── Apply initial bend transform to starting edge ─────────────────────
		float startP;
		if (bendStart > 0)
			startP = 0.0f;
		else if (bendEnd < 0)
			startP = 1.0f;
		else
			startP = 1.0f - bendEnd / bendSize;

		if (invAngle) startP = 1.0f - startP;

		Vector3 startBendVec = BendHelper.GetBendVector(b.Angle, startP);
		Transform3D startMat = BendHelper.GetBendMatrix(b, startBendVec, shapePosition, shapeRotation);

		p1 = startMat * p1;
		p2 = startMat * p2;
		// Plane normals point along Z (forward = -Z in Godot, backward = +Z)
		var n1 = (startMat.Basis * Vector3.Forward).Normalized(); // front-face normal (toward -Z)
		var n2 = (startMat.Basis * Vector3.Back).Normalized();    // back-face normal (toward +Z)

		// ── Segment loop ──────────────────────────────────────────────────────
		float segPos = 0.0f;
		while (segPos < totalSize)
		{
			// Determine segment size
			float curSegSize;
			if (segPos >= bendEnd) // Past bend: one big segment to the end
				curSegSize = totalSize - segPos;
			else if (segPos < bendStart) // Before bend: one segment up to bend start
				curSegSize = Math.Min(totalSize - segPos, bendStart - segPos);
			else // Within bend: use segSize
			{
				curSegSize = segSize;
				if (segPos == 0.0f)
				{
					float fromCoord = (segAxis == 0)
						? (x1 + shapePosition.X)
						: (y1 + shapePosition.Y);
					curSegSize -= (fromCoord - bendStart) % segSize;
				}
				curSegSize = Math.Min(totalSize - segPos, curSegSize);
			}

			segPos += Math.Max(curSegSize, 0.005f);

			// Next edge points
			Vector3 np1, np2;
			float ntexp1;

			if (segAxis == 0)
			{
				// Right edge at x1+segPos
				np1 = new Vector3(x1 + segPos, y2, z1);
				np2 = new Vector3(x1 + segPos, y1, z1);
				float toff = (segPos / totalSize) * texSizeX * (textureMirror ? -1.0f : 1.0f);
				ntexp1 = tex1.X + toff;
			}
			else
			{
				// Top edge at y1+segPos
				np1 = new Vector3(x1, y1 + segPos, z1);
				np2 = new Vector3(x2, y1 + segPos, z1);
				float toff = (segPos / totalSize) * texSizeY;
				ntexp1 = tex3.Y - toff; // V slides from bottom toward top
			}

			// Compute bend weight for this segment's far edge
			float segP;
			if (segPos < bendStart)
				segP = 0.0f;
			else if (segPos >= bendEnd)
				segP = 1.0f;
			else
				segP = 1.0f - (bendEnd - segPos) / bendSize;

			if (invAngle) segP = 1.0f - segP;

			Vector3 segBendVec = BendHelper.GetBendVector(b.Angle, segP);
			Transform3D segMat = BendHelper.GetBendMatrix(b, segBendVec, shapePosition, shapeRotation);

			np1 = segMat * np1;
			np2 = segMat * np2;
			var nn1 = (segMat.Basis * Vector3.Forward).Normalized();
			var nn2 = (segMat.Basis * Vector3.Back).Normalized();

			// ── Add quad faces for this segment ───────────────────────────────
			// Each segment is a quad strip: p1/p2 = near edge, np1/np2 = far edge
			// For segAxis=X: p1=top-near, p2=bottom-near, np1=top-far, np2=bottom-far
			// For segAxis=Y: p1=left-near, p2=right-near, np1=left-far, np2=right-far
			Vector2 t1, t2, t3, t4;

			if (segAxis == 0)
			{
				// X-axis: UV slides horizontally (U changes, V stays)
				// p1=top-left, np1=top-right, np2=bottom-right, p2=bottom-left
				t1 = new Vector2(texp1,  tex1.Y);
				t2 = new Vector2(ntexp1, tex1.Y);
				t3 = new Vector2(ntexp1, tex3.Y);
				t4 = new Vector2(texp1,  tex3.Y);

				// Front face (normal toward -Z / Vector3.Forward)
				// GML: vbuffer_add_triangle(p1, np1, np2, t1, t2, t3, ...)
				//      vbuffer_add_triangle(np2, p2, p1, t3, t4, t1, ...)
				AddFaceWithUVs(vertices, normals, uvs, indices,
					p1, np1, np2, p2, n1, nn1, nn1, n1, t1, t2, t3, t4, invert);

				// Back face (normal toward +Z / Vector3.Back)
				// GML: vbuffer_add_triangle(np1, p1, np2, t2, t1, t3, ...)
				//      vbuffer_add_triangle(p2, np2, p1, t4, t3, t1, ...)
				AddFaceWithUVs(vertices, normals, uvs, indices,
					np1, p1, p2, np2, nn2, n2, n2, nn2, t2, t1, t4, t3, invert);
			}
			else
			{
				// Y-axis: UV slides vertically (V changes, U stays)
				// p1=bottom-left, p2=bottom-right, np1=top-left, np2=top-right
				t1 = new Vector2(tex1.X, ntexp1);
				t2 = new Vector2(tex2.X, ntexp1);
				t3 = new Vector2(tex2.X, texp1);
				t4 = new Vector2(tex1.X, texp1);

				// Front face
				// GML: vbuffer_add_triangle(np1, np2, p2, t1, t2, t3, ...)
				//      vbuffer_add_triangle(p2, p1, np1, t3, t4, t1, ...)
				AddFaceWithUVs(vertices, normals, uvs, indices,
					np1, np2, p2, p1, nn1, nn1, n1, n1, t1, t2, t3, t4, invert);

				// Back face
				// GML: vbuffer_add_triangle(np2, np1, p2, t2, t1, t3, ...)
				//      vbuffer_add_triangle(p1, p2, np1, t4, t3, t1, ...)
				AddFaceWithUVs(vertices, normals, uvs, indices,
					np2, np1, p1, p2, nn2, nn2, n2, n2, t2, t1, t4, t3, invert);
			}

			p1 = np1; p2 = np2;
			n1 = nn1; n2 = nn2;
			texp1 = ntexp1;
		}

		// Build mesh
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV]  = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index]  = indices.ToArray();

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

		return new MeshInstance3D { Mesh = arrayMesh };
	}

	/// <summary>
	/// Creates a bent extruded (3D) plane mesh with per-pixel alpha-based geometry and bend deformation.
	/// Matches Modelbench's model_shape_generate_plane_3d() algorithm.
	///
	/// The algorithm pre-computes a 2D grid of bent vertex positions (one column per pixel along the
	/// bend axis), then generates up to 6 faces per opaque pixel using those bent positions.
	///
	/// Coordinate mapping (Modelbench Z-up → Godot Y-up):
	///   LEFT/RIGHT  → bend along X: outer=Y, inner=X
	///   Others      → bend along Y: outer=X, inner=Y
	/// </summary>
	private MeshInstance3D CreateBentExtrudedPlaneMesh(Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, int texWidth, int texHeight, ImageTexture texture,
		bool textureMirror, bool invert, float inflate, BendParams bend, Vector3 shapePosition, Vector3 shapeRotation = default)
	{
		if (texture == null)
			return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
				textureMirror, invert, inflate, bend, shapePosition, shapeRotation);

		var image = texture.GetImage();
		if (image == null)
			return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
				textureMirror, invert, inflate, bend, shapePosition, shapeRotation);

		// ── UV region ─────────────────────────────────────────────────────────
		int uvStartX = (int)uvU;
		int uvStartY = (int)uvV;
		int uvEndX   = (int)(uvU + sizeX);
		int uvEndY   = (int)(uvV + sizeY);

		uvStartX = Math.Max(0, Math.Min(uvStartX, texWidth  - 1));
		uvStartY = Math.Max(0, Math.Min(uvStartY, texHeight - 1));
		uvEndX   = Math.Max(0, Math.Min(uvEndX,   texWidth));
		uvEndY   = Math.Max(0, Math.Min(uvEndY,   texHeight));

		int regionW = uvEndX - uvStartX; // pixel columns (X)
		int regionH = uvEndY - uvStartY; // pixel rows    (Y in texture = height in 3D)

		if (regionW <= 0 || regionH <= 0)
			return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
				textureMirror, invert, inflate, bend, shapePosition, shapeRotation);

		// ── Geometry extents ──────────────────────────────────────────────────
		float x1 = Math.Min(from.X, to.X);
		float x2 = Math.Max(from.X, to.X);
		float y1 = Math.Min(from.Y, to.Y);
		float y2 = Math.Max(from.Y, to.Y);
		float z1 = from.Z; // plane is flat on Z

		// Pixel scale in Godot units
		float pixScaleX = (x2 - x1) / regionW;
		float pixScaleY = (y2 - y1) / regionH;

		// Extrusion half-thickness (1 pixel = 1/16 block)
		const float thickness = 1.0f / 16.0f;
		float halfT = thickness / 2.0f + inflate;

		// ── Bend parameters ───────────────────────────────────────────────────
		var b = bend;

		// Segment axis in Godot Y-up:
		//   LEFT/RIGHT  → inner=X (0), outer=Y (1)
		//   Others      → inner=Y (1), outer=X (0)
		bool bendAlongX = (b.Part == BendPart.Left || b.Part == BendPart.Right);
		// innerAxis = axis along which bend is applied (columns)
		// outerAxis = axis perpendicular to bend (rows)

		float bendSize   = b.BendSize   / 16.0f;
		float bendOffset = b.BendOffset / 16.0f;
		bool invAngle = (b.Part == BendPart.Lower || b.Part == BendPart.Back || b.Part == BendPart.Left);

		// Bend region start/end
		float bendStart, bendEnd;
		if (bendAlongX)
		{
			bendStart = (bendOffset - (shapePosition.X + x1)) - bendSize / 2.0f;
			bendEnd   = (bendOffset - (shapePosition.X + x1)) + bendSize / 2.0f;
		}
		else
		{
			bendStart = (bendOffset - (shapePosition.Y + y1)) - bendSize / 2.0f;
			bendEnd   = (bendOffset - (shapePosition.Y + y1)) + bendSize / 2.0f;
		}

		// ── Pre-compute bent vertex grid ──────────────────────────────────────
		// Grid dimensions: (outerCount+1) × (innerCount+1) vertex positions
		// Each grid point has a "bottom" (y1) and "top" (y2) position in 3D.
		// For bendAlongX: outer=Y rows (regionH+1), inner=X cols (regionW+1)
		// For bendAlongY: outer=X cols (regionW+1), inner=Y rows (regionH+1)
		int outerCount = bendAlongX ? regionH : regionW;
		int innerCount = bendAlongX ? regionW : regionH;

		// gridBot[outer, inner] = bottom vertex (y1 side)
		// gridTop[outer, inner] = top vertex    (y2 side)
		var gridBot = new Vector3[outerCount + 1, innerCount + 1];
		var gridTop = new Vector3[outerCount + 1, innerCount + 1];

		for (int outer = 0; outer <= outerCount; outer++)
		{
			for (int inner = 0; inner <= innerCount; inner++)
			{
				Vector3 pBot, pTop;
				if (bendAlongX)
				{
					// inner=X, outer=Y
					float px = x1 + inner * pixScaleX;
					float pyBot = y1 + outer * pixScaleY;
					float pyTop = y1 + outer * pixScaleY; // same Y for both (plane is flat on Z)
					// "bot" = z1-halfT side, "top" = z1+halfT side
					pBot = new Vector3(px, pyBot, z1 - halfT);
					pTop = new Vector3(px, pyTop, z1 + halfT);
				}
				else
				{
					// inner=Y, outer=X
					float px = x1 + outer * pixScaleX;
					float pyBot = y1 + inner * pixScaleY;
					float pyTop = y1 + inner * pixScaleY;
					pBot = new Vector3(px, pyBot, z1 - halfT);
					pTop = new Vector3(px, pyTop, z1 + halfT);
				}

				// Apply bend transform based on inner position (along bend axis)
				float innerPos = inner * (bendAlongX ? pixScaleX : pixScaleY);
				float segP;
				if (innerPos < bendStart)
					segP = 0.0f;
				else if (innerPos >= bendEnd)
					segP = 1.0f;
				else
					segP = 1.0f - (bendEnd - innerPos) / bendSize;

				if (invAngle) segP = 1.0f - segP;

				Vector3 bendVec = BendHelper.GetBendVector(b.Angle, segP);
				Transform3D mat = BendHelper.GetBendMatrix(b, bendVec, shapePosition, shapeRotation);

				gridBot[outer, inner] = mat * pBot;
				gridTop[outer, inner] = mat * pTop;
			}
		}

		// ── Generate per-pixel faces ──────────────────────────────────────────
		var vertices = new List<Vector3>();
		var normals  = new List<Vector3>();
		var uvs      = new List<Vector2>();
		var indices  = new List<int>();

		float texNormW = 1.0f / texWidth;
		float texNormH = 1.0f / texHeight;
		float ptexSizeX = (1.0f - 1.0f / 256.0f) / (texWidth  / (sizeX / regionW));
		float ptexSizeY = (1.0f - 1.0f / 256.0f) / (texHeight / (sizeY / regionH));

		for (int outer = 0; outer < outerCount; outer++)
		{
			for (int inner = 0; inner < innerCount; inner++)
			{
				// Map (outer, inner) to texture pixel (ax, ay)
				int ax, ay;
				if (bendAlongX)
				{
					// inner=X, outer=Y; texture row 0 = top of image = top of 3D (high Y)
					ax = textureMirror ? (regionW - 1 - inner) : inner;
					ay = regionH - 1 - outer; // flip Y: outer=0 → bottom of 3D → last row of texture
				}
				else
				{
					// inner=Y, outer=X
					ax = textureMirror ? (regionW - 1 - outer) : outer;
					ay = regionH - 1 - inner;
				}

				int texX = uvStartX + ax;
				int texY = uvStartY + ay;

				if (texX >= image.GetWidth() || texY >= image.GetHeight()) continue;
				var color = image.GetPixel(texX, texY);
				if (color.A <= 0.5f) continue;

				// UV for this pixel
				float uvX = (texX + 0.5f) * texNormW;
				float uvY = (texY + 0.5f) * texNormH;
				var pixUV = new Vector2(uvX, uvY);

				// The 8 corners of this pixel's bent box
				// p1..p4 = near edge (inner), np1..np4 = far edge (inner+1)
				// Bot = z1-halfT side, Top = z1+halfT side
				Vector3 p1, p2, p3, p4, np1, np2, np3, np4;

				if (bendAlongX)
				{
					// seginneraxis=X in GML
					// p1=gridBot[outer+1, inner],  p2=gridTop[outer+1, inner]
					// p3=gridTop[outer,   inner],  p4=gridBot[outer,   inner]
					// np1=gridBot[outer+1, inner+1], np2=gridTop[outer+1, inner+1]
					// np3=gridTop[outer,   inner+1], np4=gridBot[outer,   inner+1]
					p1  = gridBot[outer + 1, inner];
					p2  = gridTop[outer + 1, inner];
					p3  = gridTop[outer,     inner];
					p4  = gridBot[outer,     inner];
					np1 = gridBot[outer + 1, inner + 1];
					np2 = gridTop[outer + 1, inner + 1];
					np3 = gridTop[outer,     inner + 1];
					np4 = gridBot[outer,     inner + 1];
				}
				else
				{
					// seginneraxis=Z in GML (→ Y in Godot)
					// p1=gridBot[outer,   inner],  p2=gridBot[outer+1, inner]
					// p3=gridTop[outer+1, inner],  p4=gridTop[outer,   inner]
					// np1=gridBot[outer,   inner+1], np2=gridBot[outer+1, inner+1]
					// np3=gridTop[outer+1, inner+1], np4=gridTop[outer,   inner+1]
					p1  = gridBot[outer,     inner];
					p2  = gridBot[outer + 1, inner];
					p3  = gridTop[outer + 1, inner];
					p4  = gridTop[outer,     inner];
					np1 = gridBot[outer,     inner + 1];
					np2 = gridBot[outer + 1, inner + 1];
					np3 = gridTop[outer + 1, inner + 1];
					np4 = gridTop[outer,     inner + 1];
				}

				// Determine which edge faces to add (based on adjacent pixel alpha)
				bool leftEmpty   = (ax == 0)           || image.GetPixel(uvStartX + ax - 1, texY).A <= 0.5f;
				bool rightEmpty  = (ax == regionW - 1) || image.GetPixel(uvStartX + ax + 1, texY).A <= 0.5f;
				bool topEmpty    = (ay == 0)           || image.GetPixel(texX, uvStartY + ay - 1).A <= 0.5f;
				bool bottomEmpty = (ay == regionH - 1) || image.GetPixel(texX, uvStartY + ay + 1).A <= 0.5f;

				// In GML: wface = left in texture, eface = right in texture
				// When mirrored, east/west are swapped
				bool wface = textureMirror ? rightEmpty : leftEmpty;
				bool eface = textureMirror ? leftEmpty  : rightEmpty;
				bool aface = topEmpty;    // "above" in texture = top of image = high Y in 3D
				bool bface = bottomEmpty; // "below" in texture = bottom of image = low Y in 3D

				// NOTE: GameMaker uses a left-handed coordinate system; Godot uses right-handed.
				// This means the winding order is reversed: GML CCW → Godot CW (back face),
				// so we reverse all vertex orders from the GML to get correct Godot front faces.
				if (bendAlongX)
				{
					// seginneraxis=X in GML (reversed for Godot right-hand coords):
					if (eface) AddSimpleQuad(vertices, normals, uvs, indices, np3, np4, np1, np2, pixUV, invert);
					if (wface) AddSimpleQuad(vertices, normals, uvs, indices, p4,  p3,  p2,  p1,  pixUV, invert);
					// South (front, Z+ = gridTop): reversed from GML p2,np2,np3,p3
					AddSimpleQuad(vertices, normals, uvs, indices, p3,  np3, np2, p2,  pixUV, invert);
					// North (back, Z- = gridBot): reversed from GML np1,p1,p4,np4
					AddSimpleQuad(vertices, normals, uvs, indices, np4, p4,  p1,  np1, pixUV, invert);
					if (aface) AddSimpleQuad(vertices, normals, uvs, indices, p2,  np2, np1, p1,  pixUV, invert);
					if (bface) AddSimpleQuad(vertices, normals, uvs, indices, p4,  np4, np3, p3,  pixUV, invert);
				}
				else
				{
					// seginneraxis=Z (→Y in Godot) in GML (reversed for Godot right-hand coords):
					if (eface) AddSimpleQuad(vertices, normals, uvs, indices, p3,  p2,  np2, np3, pixUV, invert);
					if (wface) AddSimpleQuad(vertices, normals, uvs, indices, p1,  p4,  np4, np1, pixUV, invert);
					// South (front, Z+ = gridTop): reversed from GML np4,np3,p3,p4
					AddSimpleQuad(vertices, normals, uvs, indices, p4,  p3,  np3, np4, pixUV, invert);
					// North (back, Z- = gridBot): reversed from GML np2,np1,p1,p2
					AddSimpleQuad(vertices, normals, uvs, indices, p2,  p1,  np1, np2, pixUV, invert);
					if (aface) AddSimpleQuad(vertices, normals, uvs, indices, np4, np3, np2, np1, pixUV, invert);
					if (bface) AddSimpleQuad(vertices, normals, uvs, indices, p1,  p2,  p3,  p4,  pixUV, invert);
				}
			}
		}

		if (vertices.Count == 0)
			return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
				textureMirror, invert, inflate, bend, shapePosition, shapeRotation);

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV]  = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index]  = indices.ToArray();

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return new MeshInstance3D { Mesh = arrayMesh };
	}

	/// <summary>
	/// Adds a simple quad (4 vertices, 2 triangles) with a single UV coordinate for all vertices.
	/// Used by CreateBentExtrudedPlaneMesh for per-pixel face generation.
	/// Uses the same winding convention as AddFaceWithUVs (indices 0,2,1 and 0,3,2 for normal,
	/// reversed when invert=true).
	/// The normal is computed from the cross product of edges (v2-v0) × (v1-v0).
	/// </summary>
	private void AddSimpleQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
		List<int> indices, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv, bool invert)
	{
		// Compute face normal matching the winding convention used by AddFaceWithUVs.
		// Indices 0,2,1 means the rendered triangle is v0,v2,v1.
		// For that winding, the outward normal = (v1-v0) × (v2-v0) (negated from the triangle order).
		var edge1 = v1 - v0;
		var edge2 = v2 - v0;
		var normal = edge1.Cross(edge2).Normalized();
		if (normal == Vector3.Zero) normal = Vector3.Up;

		int baseVertex = vertices.Count;
		vertices.Add(v0); vertices.Add(v1); vertices.Add(v2); vertices.Add(v3);
		normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
		uvs.Add(uv); uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);

		// Match AddFaceWithUVs winding: 0,2,1 and 0,3,2 (normal), 0,1,2 and 0,2,3 (inverted)
		if (invert)
		{
			indices.Add(baseVertex + 0); indices.Add(baseVertex + 1); indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 0); indices.Add(baseVertex + 2); indices.Add(baseVertex + 3);
		}
		else
		{
			indices.Add(baseVertex + 0); indices.Add(baseVertex + 2); indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 0); indices.Add(baseVertex + 3); indices.Add(baseVertex + 2);
		}
	}

	/// <summary>
	/// Creates a plane mesh (single face with both front and back for proper two-sided rendering)
	/// </summary>
	private MeshInstance3D CreatePlaneMesh(Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, int texWidth, int texHeight, 
		bool textureMirror, bool invert, float inflate = 0.0f)
	{
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var uvs = new List<Vector2>();
		var indices = new List<int>();
		
		// Ensure proper ordering
		Vector3 min = new Vector3(
			Math.Min(from.X, to.X),
			Math.Min(from.Y, to.Y),
			Math.Min(from.Z, to.Z)
		);
		Vector3 max = new Vector3(
			Math.Max(from.X, to.X),
			Math.Max(from.Y, to.Y),
			Math.Max(from.Z, to.Z)
		);
		
		// Apply inflate - expand the mesh bounds by the inflate amount in all directions
		if (inflate != 0.0f)
		{
			min = new Vector3(min.X - inflate, min.Y - inflate, min.Z - inflate);
			max = new Vector3(max.X + inflate, max.Y + inflate, max.Z + inflate);
		}
		
		// Convert sizes to normalized UV coordinates (0-1 range)
		// Using the same UV layout as Mine-imator GML project for planes
		float texU = uvU / texWidth;
		float texV = uvV / texHeight;
		
		// Texture sizes normalized (plane uses X and Z dimensions like south face of block)
		float texSizeX = sizeX / texWidth;
		float texSizeZ = sizeY / texHeight; // sizeY represents Z dimension for planes
		
		// Plane texture mapping (following Mine-imator layout):
		// tex1 = UV origin
		// tex2 = right of origin by X
		// tex3 = right by X, down by Z
		// tex4 = down from origin by Z
		var tex1 = new Vector2(texU, texV);
		var tex2 = new Vector2(texU + texSizeX, texV);
		var tex3 = new Vector2(texU + texSizeX, texV + texSizeZ);
		var tex4 = new Vector2(texU, texV + texSizeZ);
		
		// Mirror texture on X if needed - this means flipping the geometry on X axis
		// to create a mirrored version of the plane
		if (textureMirror)
		{
			// Switch left/right points for UV mirroring
			(tex1, tex2) = (tex2, tex1);
			(tex3, tex4) = (tex4, tex3);
		}
		
		// Front face (at min.Z, normal pointing backward)
		int baseVertex = vertices.Count;
		
		vertices.Add(new Vector3(min.X, min.Y, min.Z));
		vertices.Add(new Vector3(max.X, min.Y, min.Z));
		vertices.Add(new Vector3(max.X, max.Y, min.Z));
		vertices.Add(new Vector3(min.X, max.Y, min.Z));
		
		normals.Add(Vector3.Back);
		normals.Add(Vector3.Back);
		normals.Add(Vector3.Back);
		normals.Add(Vector3.Back);
		
		// UV mapping: tex1=bottom-left, tex2=bottom-right, tex3=top-right, tex4=top-left
		// When inverted, flip UV coordinates horizontally to mirror the texture
		if (invert)
		{
			uvs.Add(tex3);
			uvs.Add(tex4);
			uvs.Add(tex1);
			uvs.Add(tex2);
		}
		else
		{
			uvs.Add(tex4);
			uvs.Add(tex3);
			uvs.Add(tex2);
			uvs.Add(tex1);
		}
		
		// Front face indices (counter-clockwise winding for front face)
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 2);
		indices.Add(baseVertex + 1);
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 3);
		indices.Add(baseVertex + 2);
		
		// Back face (at max.Z, normal pointing forward) - for two-sided rendering
		baseVertex = vertices.Count;
		
		vertices.Add(new Vector3(min.X, min.Y, max.Z));
		vertices.Add(new Vector3(max.X, min.Y, max.Z));
		vertices.Add(new Vector3(max.X, max.Y, max.Z));
		vertices.Add(new Vector3(min.X, max.Y, max.Z));
		
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		
		// UV mapping for back face (same orientation, but also flip when inverted for consistency)
		if (invert)
		{
			uvs.Add(tex3);
			uvs.Add(tex4);
			uvs.Add(tex1);
			uvs.Add(tex2);
		}
		else
		{
			uvs.Add(tex4);
			uvs.Add(tex3);
			uvs.Add(tex2);
			uvs.Add(tex1);
		}
		
		// Back face indices - reverse winding from front face (clockwise for back face)
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 1);
		indices.Add(baseVertex + 2);
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 2);
		indices.Add(baseVertex + 3);
		
		// Create the mesh
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var meshInstance = new MeshInstance3D
        {
            Mesh = arrayMesh
        };

        return meshInstance;
	}
	
	/// <summary>
	/// Creates an extruded plane mesh (per-pixel extrusion like items)
	/// </summary>
	private MeshInstance3D CreateExtrudedPlaneMesh(Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, int texWidth, int texHeight, ImageTexture texture,
		bool textureMirror, bool invert, float inflate = 0.0f)
	{
		if (texture == null)
		{
			// Fallback to regular plane if no texture
			return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, textureMirror, invert, inflate);
		}
		
		var image = texture.GetImage();
		if (image == null)
		{
			return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, textureMirror, invert, inflate);
		}
		
		// Calculate the UV region we're using
		int uvStartX = (int)uvU;
		int uvStartY = (int)uvV;
		int uvEndX = (int)(uvU + sizeX);
		int uvEndY = (int)(uvV + sizeY);
		
		// Clamp to texture bounds
		uvStartX = Math.Max(0, Math.Min(uvStartX, texWidth - 1));
		uvStartY = Math.Max(0, Math.Min(uvStartY, texHeight - 1));
		uvEndX = Math.Max(0, Math.Min(uvEndX, texWidth));
		uvEndY = Math.Max(0, Math.Min(uvEndY, texHeight));
		
		int regionWidth = uvEndX - uvStartX;
		int regionHeight = uvEndY - uvStartY;
		
		if (regionWidth <= 0 || regionHeight <= 0)
		{
			return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, textureMirror, invert, inflate);
		}
		
		// Extrusion thickness (1 pixel = 1/16 of a block)
		const float thickness = 1.0f / 16.0f;
		float halfThickness = thickness / 2.0f;
		
		// Apply inflate to the extrusion thickness
		if (inflate != 0.0f)
		{
			halfThickness += inflate;
		}
		
		// Calculate scale for each pixel
		Vector3 size = to - from;
		float pixelScaleX = size.X / regionWidth;
		float pixelScaleY = size.Y / regionHeight;
		
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var uvs = new List<Vector2>();
		var indices = new List<int>();
		
		// Process each pixel in the UV region
		for (int py = 0; py < regionHeight; py++)
		{
			for (int px = 0; px < regionWidth; px++)
			{
				int texX = uvStartX + px;
				int texY = uvStartY + py;
				
				// Check bounds
				if (texX >= image.GetWidth() || texY >= image.GetHeight())
					continue;
				
				var color = image.GetPixel(texX, texY);
				
				// Only create geometry for non-transparent pixels
				if (color.A > 0.5f)
				{
					// Calculate position for this pixel
					// When textureMirror is true, mirror the geometry on X axis
					float posX;
					if (textureMirror)
					{
						// Mirror: flip X position (start from right side and go left)
						posX = to.X - (px + 1) * pixelScaleX;
					}
					else
					{
						posX = from.X + px * pixelScaleX;
					}
					// Flip Y: texture row 0 is the top of the image, but in 3D Y increases upward,
					// so row 0 should map to the top of the plane (to.Y) and the last row to from.Y.
					float posY = to.Y - (py + 1) * pixelScaleY;
					
					// Apply inflate to pixel position (expand outward)
					if (inflate != 0.0f)
					{
						posX -= inflate;
						posY -= inflate;
					}
					
					// Adjust pixel scale for inflate
					float adjustedPixelScaleX = pixelScaleX;
					float adjustedPixelScaleY = pixelScaleY;
					if (inflate != 0.0f)
					{
						adjustedPixelScaleX += inflate * 2;
						adjustedPixelScaleY += inflate * 2;
					}
					
					// Center of this pixel box
					float centerX = posX + adjustedPixelScaleX / 2.0f;
					float centerY = posY + adjustedPixelScaleY / 2.0f;
					float centerZ = from.Z + (0.5f * 0.0625f); // Plane is at Z=0 by default
					
					// UV coordinates for this pixel (normalized)
					float uvX = (texX + 0.5f) / texWidth;
					float uvY = (texY + 0.5f) / texHeight;
					
					int baseVertex = vertices.Count;
					
					// Create a box for this pixel (6 faces)
					// Front face (Z+)
					AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
						new Vector3(posX, posY, centerZ + halfThickness),
						new Vector3(posX + adjustedPixelScaleX, posY, centerZ + halfThickness),
						new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ + halfThickness),
						new Vector3(posX, posY + adjustedPixelScaleY, centerZ + halfThickness),
						Vector3.Back, uvX, uvY, invert);
					
					baseVertex = vertices.Count;
					// Back face (Z-)
					AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
						new Vector3(posX + adjustedPixelScaleX, posY, centerZ - halfThickness),
						new Vector3(posX, posY, centerZ - halfThickness),
						new Vector3(posX, posY + adjustedPixelScaleY, centerZ - halfThickness),
						new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ - halfThickness),
						Vector3.Forward, uvX, uvY, invert);
					
					// Check adjacent pixels for edge faces
					// When mirrored, left/right are swapped in the geometry
					bool leftEmpty = px == 0 || image.GetPixel(uvStartX + px - 1, texY).A <= 0.5f;
					bool rightEmpty = px == regionWidth - 1 || image.GetPixel(uvStartX + px + 1, texY).A <= 0.5f;
					bool topEmpty = py == 0 || image.GetPixel(texX, uvStartY + py - 1).A <= 0.5f;
					bool bottomEmpty = py == regionHeight - 1 || image.GetPixel(texX, uvStartY + py + 1).A <= 0.5f;
					
					// When textureMirror is true, swap left/right edge detection since geometry is flipped
					bool geometryLeftEmpty = textureMirror ? rightEmpty : leftEmpty;
					bool geometryRightEmpty = textureMirror ? leftEmpty : rightEmpty;
					
					if (geometryLeftEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX, posY, centerZ - halfThickness),
							new Vector3(posX, posY, centerZ + halfThickness),
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							Vector3.Left, uvX, uvY, invert);
					}
					
					if (geometryRightEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ + halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							Vector3.Right, uvX, uvY, invert);
					}
					
					// After Y-flip: posY is the bottom of the pixel box in 3D space,
					// posY + adjustedPixelScaleY is the top.
					// topEmpty = no pixel above in texture = no pixel above in 3D (top face needed at posY + adjustedPixelScaleY)
					// bottomEmpty = no pixel below in texture = no pixel below in 3D (bottom face needed at posY)
					if (topEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							Vector3.Up, uvX, uvY, invert);
					}
					
					if (bottomEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX, posY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ + halfThickness),
							new Vector3(posX, posY, centerZ + halfThickness),
							Vector3.Down, uvX, uvY, invert);
					}
				}
			}
		}
		
		// Create the mesh
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var meshInstance = new MeshInstance3D
        {
            Mesh = arrayMesh
        };

        return meshInstance;
	}
	
	/// <summary>
	/// Adds a quad for extruded plane mesh
	/// </summary>
	private void AddExtrudedQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
		List<int> indices, int baseVertex, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
		Vector3 normal, float uvX, float uvY, bool invert)
	{
		vertices.Add(v0);
		vertices.Add(v1);
		vertices.Add(v2);
		vertices.Add(v3);
		
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
		
		// Use the pixel's texture coordinate for all vertices
		// When inverted, flip UV coordinates horizontally to mirror the texture
		if (invert)
		{
			uvs.Add(new Vector2(1.0f - uvX, uvY));
			uvs.Add(new Vector2(1.0f - uvX, uvY));
			uvs.Add(new Vector2(1.0f - uvX, uvY));
			uvs.Add(new Vector2(1.0f - uvX, uvY));
		}
		else
		{
			uvs.Add(new Vector2(uvX, uvY));
			uvs.Add(new Vector2(uvX, uvY));
			uvs.Add(new Vector2(uvX, uvY));
			uvs.Add(new Vector2(uvX, uvY));
		}
		
		// Indices (counter-clockwise winding)
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 2);
		indices.Add(baseVertex + 1);
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 3);
		indices.Add(baseVertex + 2);
	}
	
	/// <summary>
	/// Adds a face to the mesh data
	/// </summary>
	private void AddFace(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
		Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, Vector3 vertex3, Vector3 normal,
		float uvU, float uvV, float faceWidth, float faceHeight,
		int texWidth, int texHeight, bool textureMirror, bool invert)
	{
		int baseVertex = vertices.Count;
		
		vertices.Add(vertex0);
		vertices.Add(vertex1);
		vertices.Add(vertex2);
		vertices.Add(vertex3);
		
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
		
		// Calculate UV coordinates (convert pixel coordinates to 0-1 range)
		float u0 = uvU / texWidth;
		float v0 = uvV / texHeight;
		float u1 = (uvU + faceWidth) / texWidth;
		float v1 = (uvV + faceHeight) / texHeight;
		
		if (textureMirror)
		{
			(u0, u1) = (u1, u0);
		}
		
		// When inverted, flip UV coordinates horizontally to mirror the texture
		if (invert)
		{
			uvs.Add(new Vector2(u1, v1));
			uvs.Add(new Vector2(u0, v1));
			uvs.Add(new Vector2(u0, v0));
			uvs.Add(new Vector2(u1, v0));
		}
		else
		{
			uvs.Add(new Vector2(u0, v1));
			uvs.Add(new Vector2(u1, v1));
			uvs.Add(new Vector2(u1, v0));
			uvs.Add(new Vector2(u0, v0));
		}
		
		// Front face indices (counter-clockwise winding)
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 2);
		indices.Add(baseVertex + 1);
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 3);
		indices.Add(baseVertex + 2);
	}

	/// <summary>
	/// Adds a face to the mesh data with pre-calculated UV coordinates (uniform normal)
	/// </summary>
	private void AddFaceWithUVs(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
		Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, Vector3 vertex3, Vector3 normal,
		Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, bool invert)
	{
		AddFaceWithUVs(vertices, normals, uvs, indices,
			vertex0, vertex1, vertex2, vertex3,
			normal, normal, normal, normal,
			uv0, uv1, uv2, uv3, invert);
	}
	
	/// <summary>
	/// Adds a face to the mesh data with pre-calculated UV coordinates and per-vertex normals.
	/// Used for bent segments where normals are interpolated between the two edge normals.
	/// </summary>
	private void AddFaceWithUVs(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
		Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, Vector3 vertex3,
		Vector3 normal01, Vector3 normal23,
		Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, bool invert)
	{
		AddFaceWithUVs(vertices, normals, uvs, indices,
			vertex0, vertex1, vertex2, vertex3,
			normal01, normal01, normal23, normal23,
			uv0, uv1, uv2, uv3, invert);
	}
	
	/// <summary>
	/// Adds a face to the mesh data with pre-calculated UV coordinates and 4 per-vertex normals.
	/// Used for bent segment side faces where each vertex needs its own normal.
	/// </summary>
	private void AddFaceWithUVs(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
		Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, Vector3 vertex3,
		Vector3 normal0, Vector3 normal1, Vector3 normal2, Vector3 normal3,
		Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, bool invert)
	{
		int baseVertex = vertices.Count;
		
		vertices.Add(vertex0);
		vertices.Add(vertex1);
		vertices.Add(vertex2);
		vertices.Add(vertex3);
		
		// Each vertex gets its own normal for smooth shading
		normals.Add(normal0);
		normals.Add(normal1);
		normals.Add(normal2);
		normals.Add(normal3);
		
		// Use pre-calculated UV coordinates
		// When inverted, flip UV coordinates horizontally to mirror the texture
		if (invert)
		{
			uvs.Add(uv2);
			uvs.Add(uv3);
			uvs.Add(uv0);
			uvs.Add(uv1);
		}
		else
		{
			uvs.Add(uv0);
			uvs.Add(uv1);
			uvs.Add(uv2);
			uvs.Add(uv3);
		}
		
		// Indices (counter-clockwise winding)
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 2);
		indices.Add(baseVertex + 1);
		indices.Add(baseVertex + 0);
		indices.Add(baseVertex + 3);
		indices.Add(baseVertex + 2);
	}
	
	/// <summary>
	/// Loads the texture for a Mine Imator model
	/// </summary>
	private ImageTexture LoadModelTexture(MiModel model)
	{
		if (string.IsNullOrEmpty(model.Texture) || string.IsNullOrEmpty(model.DirectoryPath))
		{
			return null;
		}
		
		var texturePath = Path.Combine(model.DirectoryPath, model.Texture);
		
		if (!File.Exists(texturePath))
		{
			GD.PrintErr($"Mine Imator texture not found: {texturePath}");
			return null;
		}
		
		try
		{
			// Read file bytes using System.IO (works for external paths)
			byte[] fileBytes = File.ReadAllBytes(texturePath);
			
			// Check for PNG header
			if (fileBytes.Length >= 8)
			{
				bool isPng = fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && 
				             fileBytes[2] == 0x4E && fileBytes[3] == 0x47;
			}
			
			var image = new Image();
			Error error;
			
			// Determine image format from extension and load from buffer
			string extension = Path.GetExtension(texturePath).ToLowerInvariant();
			switch (extension)
			{
				case ".png":
					error = image.LoadPngFromBuffer(fileBytes);
					break;
				case ".jpg":
				case ".jpeg":
					error = image.LoadJpgFromBuffer(fileBytes);
					break;
				case ".webp":
					error = image.LoadWebpFromBuffer(fileBytes);
					break;
				case ".bmp":
					error = image.LoadBmpFromBuffer(fileBytes);
					break;
				case ".tga":
					error = image.LoadTgaFromBuffer(fileBytes);
					break;
				default:
					// Try PNG first, then other formats
					error = image.LoadPngFromBuffer(fileBytes);
					if (error != Error.Ok)
					{
						error = image.LoadJpgFromBuffer(fileBytes);
					}
					if (error != Error.Ok)
					{
						error = image.LoadWebpFromBuffer(fileBytes);
					}
					if (error != Error.Ok)
					{
						error = image.LoadBmpFromBuffer(fileBytes);
					}
					if (error != Error.Ok)
					{
						error = image.LoadTgaFromBuffer(fileBytes);
					}
					break;
			}
			
			if (image.IsEmpty())
			{
				GD.PrintErr($"Mine Imator texture loaded but image is empty: {texturePath}");
				return null;
			}
			
			var texture = ImageTexture.CreateFromImage(image);
			
			return texture;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception loading Mine Imator texture '{texturePath}': {ex.Message}");
			return null;
		}
	}
	
	/// <summary>
	/// Clears the model cache
	/// </summary>
	public void ClearCache()
	{
		_modelCache.Clear();
	}
}

#region Model Data Classes

/// <summary>
/// Represents a Mine Imator model
/// </summary>
public class MiModel
{
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("texture")]
	public string Texture { get; set; }
	
	[JsonPropertyName("texture_size")]
	public int[] TextureSize { get; set; }
	
	[JsonPropertyName("parts")]
	public List<MiPart> Parts { get; set; }
	
	// Runtime properties (not in JSON)
	[JsonIgnore]
	public string DirectoryPath { get; set; }
	
	[JsonIgnore]
	public string FullPath { get; set; }
}

/// <summary>
/// Represents a part in a Mine Imator model (acts as a bone)
/// </summary>
public class MiPart
{
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("texture")]
	public string Texture { get; set; }
	
	[JsonPropertyName("texture_size")]
	public int[] TextureSize { get; set; }
	
	[JsonPropertyName("position")]
	public float[] Position { get; set; }
	
	[JsonPropertyName("rotation")]
	public float[] Rotation { get; set; }
	
	[JsonPropertyName("scale")]
	public float[] Scale { get; set; }
	
	[JsonPropertyName("bend")]
	public MiBend Bend { get; set; }
	
	[JsonPropertyName("lock_bend")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
	public float? LockBend { get; set; }
	
	[JsonPropertyName("locked")]
	public bool Locked { get; set; }
	
	[JsonPropertyName("shapes")]
	public List<MiShape> Shapes { get; set; }
	
	[JsonPropertyName("parts")]
	public List<MiPart> Parts { get; set; }
}

/// <summary>
/// Represents a shape in a Mine Imator model part
/// </summary>
public class MiShape
{
	[JsonPropertyName("type")]
	public string Type { get; set; } = "block";
	
	[JsonPropertyName("from")]
	public float[] From { get; set; }
	
	[JsonPropertyName("to")]
	public float[] To { get; set; }
	
	[JsonPropertyName("uv")]
	public float[] Uv { get; set; }
	
	[JsonPropertyName("position")]
	public float[] Position { get; set; }
	
	[JsonPropertyName("rotation")]
	public float[] Rotation { get; set; }
	
	[JsonPropertyName("scale")]
	public float[] Scale { get; set; }
	
	[JsonPropertyName("invert")]
	public bool Invert { get; set; }
	
	[JsonPropertyName("texture_mirror")]
	public bool TextureMirror { get; set; }
	
	[JsonPropertyName("3d")]
	public bool ThreeD { get; set; }
	
	[JsonPropertyName("inflate")]
	public float Inflate { get; set; }
	
	/// <summary>
	/// Whether this shape participates in the parent part's bending deformation.
	/// Corresponds to Modelbench's BEND_SHAPE value.
	/// </summary>
	[JsonPropertyName("bend")]
	public bool Bend { get; set; } = true;
}

/// <summary>
/// Represents bend settings for a Mine Imator model part
/// </summary>
public class MiBend
{
	[JsonPropertyName("offset")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
	public float? Offset { get; set; }
	
	[JsonPropertyName("end_offset")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
	public float? EndOffset { get; set; }
	
	[JsonPropertyName("size")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
	public float? Size { get; set; }
	
	[JsonPropertyName("detail")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
	public float? Detail { get; set; }
	
	[JsonPropertyName("inherit_bend")]
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
	public float? InheritBend { get; set; }
	
	[JsonPropertyName("part")]
	public string Part { get; set; }
	
	[JsonPropertyName("axis")]
	public object Axis { get; set; } // Can be string[] or string
	
	[JsonPropertyName("direction_min")]
	[JsonConverter(typeof(SingleOrArrayConverter))]
	public float[] DirectionMin { get; set; } // Can be single float or array of floats
	
	[JsonPropertyName("direction_max")]
	[JsonConverter(typeof(SingleOrArrayConverter))]
	public float[] DirectionMax { get; set; } // Can be single float or array of floats
	
	[JsonPropertyName("angle")]
	[JsonConverter(typeof(SingleOrArrayConverter))]
	public float[] Angle { get; set; } // Default bend angle(s)
	
	[JsonPropertyName("invert")]
	[JsonConverter(typeof(SingleOrArrayBoolConverter))]
	public bool[] Invert { get; set; } // Invert bend direction per axis
}

/// <summary>
/// Custom JSON converter that handles values that can be either a single float or an array of floats
/// </summary>
public class SingleOrArrayConverter : JsonConverter<float[]>
{
	public override float[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Number)
		{
			return new float[] { reader.GetSingle() };
		}
		else if (reader.TokenType == JsonTokenType.StartArray)
		{
			var list = new List<float>();
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndArray)
					break;
				if (reader.TokenType == JsonTokenType.Number)
					list.Add(reader.GetSingle());
			}
			return list.ToArray();
		}
		else if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		throw new JsonException($"Unexpected token type {reader.TokenType} when parsing float or float array");
	}

	public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else if (value.Length == 1)
		{
			writer.WriteNumberValue(value[0]);
		}
		else
		{
			writer.WriteStartArray();
			foreach (var v in value)
			{
				writer.WriteNumberValue(v);
			}
			writer.WriteEndArray();
		}
	}
}

/// <summary>
/// Custom JSON converter that handles values that can be either a single bool or an array of bools
/// </summary>
public class SingleOrArrayBoolConverter : JsonConverter<bool[]>
{
	public override bool[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
		{
			return new bool[] { reader.GetBoolean() };
		}
		else if (reader.TokenType == JsonTokenType.Number)
		{
			// Handle numeric 0/1 as bool
			return new bool[] { reader.GetInt32() != 0 };
		}
		else if (reader.TokenType == JsonTokenType.StartArray)
		{
			var list = new List<bool>();
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndArray)
					break;
				if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
					list.Add(reader.GetBoolean());
				else if (reader.TokenType == JsonTokenType.Number)
					list.Add(reader.GetInt32() != 0);
			}
			return list.ToArray();
		}
		else if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}
		throw new JsonException($"Unexpected token type {reader.TokenType} when parsing bool or bool array");
	}

	public override void Write(Utf8JsonWriter writer, bool[] value, JsonSerializerOptions options)
	{
		if (value == null)
		{
			writer.WriteNullValue();
		}
		else if (value.Length == 1)
		{
			writer.WriteBooleanValue(value[0]);
		}
		else
		{
			writer.WriteStartArray();
			foreach (var v in value)
				writer.WriteBooleanValue(v);
			writer.WriteEndArray();
		}
	}
}

#endregion
