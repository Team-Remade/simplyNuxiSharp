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
			GD.Print($"Loaded Mine Imator model: {model.Name}");
			
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

		// Configure bend data on BoneSceneObjects and add meshes
		foreach (var (part, boneIdx, parentIdx, accumulatedParentScale) in boneDataList)
		{
			string boneName = skeleton.GetBoneName(boneIdx);
			if (!character.BoneObjects.TryGetValue(boneName, out var boneObject))
				continue;

			// Set up bend configuration on the bone
			if (part.Bend != null)
			{
				float[] partScale = part.Scale;
				var bendConfig = BendHelper.ParseBendConfig(part.Bend, partScale);
				if (bendConfig != null)
				{
					boneObject.BendConfig = bendConfig;
					boneObject.LockBend = part.LockBend == null || part.LockBend.Value != 0;
					boneObject.InheritBend = (part.Bend.InheritBend ?? 0) != 0;

					// Set the default bend angles
					boneObject.BendAngles = new Vector3(
						bendConfig.DefaultAngle[0],
						bendConfig.DefaultAngle[1],
						bendConfig.DefaultAngle[2]
					);
				}
			}

			// Add meshes to the BoneSceneObject
			if (part.Shapes != null && part.Shapes.Count > 0)
			{
				// Compute this part's own scale
				Vector3 partScaleVec = Vector3.One;
				if (part.Scale != null && part.Scale.Length >= 3)
				{
					partScaleVec = new Vector3(part.Scale[0], part.Scale[1], part.Scale[2]);
				}
				// Full accumulated scale for shapes = parent accumulated scale * this part's scale
				Vector3 accumulatedScale = accumulatedParentScale * partScaleVec;

				int shapeIndex = 0;
				foreach (var shape in part.Shapes)
				{
					var meshInstance = CreateShapeMesh(part.Name, shapeIndex, shape, model, texture, accumulatedScale, part.Bend);
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
		
		GD.Print($"Created Mine Imator character: {model.Name} with {skeleton.GetBoneCount()} bones");
		
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
			var boneObject = new BoneSceneObject(skeleton, i);
			boneObject.Name = boneName;
			boneObject.ObjectType = "Bone";
			
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
	/// <param name="partBend">Optional bend data from the parent part</param>
	private MeshInstance3D CreateShapeMesh(string partName, int shapeIndex, MiShape shape, MiModel model, ImageTexture texture, Vector3 accumulatedParentScale, MiBend partBend = null)
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
		Vector3 shapeRotation = Vector3.Zero;
		if (shape.Rotation != null && shape.Rotation.Length >= 3)
		{
			shapeRotation = new Vector3(Mathf.DegToRad(shape.Rotation[0]), Mathf.DegToRad(shape.Rotation[1]), Mathf.DegToRad(shape.Rotation[2]));
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
		
		MeshInstance3D meshInstance;
		
		// Check if we have bend data and should create a bent mesh
		bool hasBend = partBend != null && partBend.Offset.HasValue && !string.IsNullOrEmpty(partBend.Part);
		
		if (shape.Type == "plane")
		{
			// Treat 3D planes like items - as extruded planes with per-pixel extrusion
			if (shape.ThreeD)
			{
				// Create an extruded item-like plane with per-pixel hull mesh
				meshInstance = CreateExtrudedPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, texture, shape.TextureMirror, shape.Invert, inflate);
			}
			else if (hasBend)
			{
				// Bent plane mesh
				meshInstance = CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate, partBend, shapeScale);
			}
			else
			{
				// Regular 2D plane
				meshInstance = CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate);
			}
		}
		else // "block" or default
	 {
	 	if (hasBend)
	 	{
	 		// Create bent block mesh with default bend angle baked in
	 		meshInstance = CreateBentBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate, partBend, shapeScale);
	 	}
	 	else
	 	{
	 		meshInstance = CreateBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate);
	 	}
	 }
		
		// Apply shape scale to the mesh instance (only if not using bent mesh which handles scale internally)
		if (meshInstance != null && !hasBend)
		{
			meshInstance.Position = shapePosition;
			meshInstance.Rotation = shapeRotation;
			meshInstance.Scale = shapeScale;
		}
		else if (meshInstance != null && hasBend)
		{
			// For bent meshes, still apply position and rotation but not scale (handled in bend)
			meshInstance.Position = shapePosition;
			meshInstance.Rotation = shapeRotation;
		}
		
		// Apply material with texture
		if (meshInstance != null && texture != null)
		{
			var material = new StandardMaterial3D();
			material.AlbedoTexture = texture;
			material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
			// Use backface culling by default, frontface culling when inverted
			material.CullMode = BaseMaterial3D.CullModeEnum.Back;
			material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
			material.AlphaScissorThreshold = 0.5f;
			
			if (meshInstance.Mesh is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
			{
				arrayMesh.SurfaceSetMaterial(0, material);
			}
			
			meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
		}
		
		return meshInstance;
	}
	
	/// <summary>
	/// Creates a block (cube) mesh
	/// </summary>
	private MeshInstance3D CreateBlockMesh(string partName, int shapeIndex, Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, float sizeZ, int texWidth, int texHeight, 
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
		
		// Debug: Print UV pixel coordinates for each face
		GD.Print($"Shape UV Debug - Part: {partName}, ShapeIndex: {shapeIndex}, From: ({from.X * 16},{from.Y * 16},{from.Z * 16}), To: ({to.X * 16},{to.Y * 16},{to.Z * 16}), Size: {sizeX}x{sizeY}x{sizeZ}, Texture: {texWidth}x{texHeight}");
		GD.Print($"  South: {texSouth1.X * texWidth},{texSouth1.Y * texHeight} -> {texSouth2.X * texWidth},{texSouth2.Y * texHeight} -> {texSouth3.X * texWidth},{texSouth3.Y * texHeight} -> {texSouth4.X * texWidth},{texSouth4.Y * texHeight}");
		GD.Print($"  East:  {texEast1.X * texWidth},{texEast1.Y * texHeight} -> {texEast2.X * texWidth},{texEast2.Y * texHeight} -> {texEast3.X * texWidth},{texEast3.Y * texHeight} -> {texEast4.X * texWidth},{texEast4.Y * texHeight}");
		GD.Print($"  West:  {texWest1.X * texWidth},{texWest1.Y * texHeight} -> {texWest2.X * texWidth},{texWest2.Y * texHeight} -> {texWest3.X * texWidth},{texWest3.Y * texHeight} -> {texWest4.X * texWidth},{texWest4.Y * texHeight}");
		GD.Print($"  North: {texNorth1.X * texWidth},{texNorth1.Y * texHeight} -> {texNorth2.X * texWidth},{texNorth2.Y * texHeight} -> {texNorth3.X * texWidth},{texNorth3.Y * texHeight} -> {texNorth4.X * texWidth},{texNorth4.Y * texHeight}");
		GD.Print($"  Up:    {texUp1.X * texWidth},{texUp1.Y * texHeight} -> {texUp2.X * texWidth},{texUp2.Y * texHeight} -> {texUp3.X * texWidth},{texUp3.Y * texHeight} -> {texUp4.X * texWidth},{texUp4.Y * texHeight}");
		GD.Print($"  Down:  {texDown1.X * texWidth},{texDown1.Y * texHeight} -> {texDown2.X * texWidth},{texDown2.Y * texHeight} -> {texDown3.X * texWidth},{texDown3.Y * texHeight} -> {texDown4.X * texWidth},{texDown4.Y * texHeight}");
		
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
		
		// South face (Front, Z+)
		AddFaceWithUVs(vertices, normals, uvs, indices,
			new Vector3(min.X, min.Y, max.Z), // bottom-left
			new Vector3(max.X, min.Y, max.Z), // bottom-right
			new Vector3(max.X, max.Y, max.Z), // top-right
			new Vector3(min.X, max.Y, max.Z), // top-left
			Vector3.Back,
			texSouth4, texSouth3, texSouth2, texSouth1,
			invert);
		
		// East face (Right, X+)
		AddFaceWithUVs(vertices, normals, uvs, indices,
			new Vector3(max.X, min.Y, max.Z),  // bottom-left (from south perspective)
			new Vector3(max.X, min.Y, min.Z),  // bottom-right
			new Vector3(max.X, max.Y, min.Z),  // top-right
			new Vector3(max.X, max.Y, max.Z),  // top-left
			Vector3.Right,
			texEast4, texEast3, texEast2, texEast1,
			invert);
		
		// West face (Left, X-)
		AddFaceWithUVs(vertices, normals, uvs, indices,
			new Vector3(min.X, min.Y, min.Z),  // bottom-left
			new Vector3(min.X, min.Y, max.Z),  // bottom-right
			new Vector3(min.X, max.Y, max.Z),  // top-right
			new Vector3(min.X, max.Y, min.Z),  // top-left
			Vector3.Left,
			texWest4, texWest3, texWest2, texWest1,
			invert);
		
		// North face (Back, Z-)
		AddFaceWithUVs(vertices, normals, uvs, indices,
			new Vector3(max.X, min.Y, min.Z),  // bottom-left
			new Vector3(min.X, min.Y, min.Z),  // bottom-right
			new Vector3(min.X, max.Y, min.Z),  // top-right
			new Vector3(max.X, max.Y, min.Z),  // top-left
			Vector3.Forward,
			texNorth4, texNorth3, texNorth2, texNorth1,
			invert);
		
		// Up face (Top, Y+)
		AddFaceWithUVs(vertices, normals, uvs, indices,
			new Vector3(min.X, max.Y, max.Z),  // bottom-left (from top perspective)
			new Vector3(max.X, max.Y, max.Z),  // bottom-right
			new Vector3(max.X, max.Y, min.Z),  // top-right
			new Vector3(min.X, max.Y, min.Z),  // top-left
			Vector3.Up,
			texUp4, texUp3, texUp2, texUp1,
			invert);
		
		// Down face (Bottom, Y-) - note: UVs are flipped for down face
		AddFaceWithUVs(vertices, normals, uvs, indices,
			new Vector3(min.X, min.Y, min.Z),  // bottom-left
			new Vector3(max.X, min.Y, min.Z),  // bottom-right
			new Vector3(max.X, min.Y, max.Z),  // top-right
			new Vector3(min.X, min.Y, max.Z),  // top-left
			Vector3.Down,
			texDown4, texDown3, texDown2, texDown1,
			invert);
		
		// Create the mesh
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		
		var meshInstance = new MeshInstance3D();
		meshInstance.Mesh = arrayMesh;
		
		return meshInstance;
	}
	
	/// <summary>
	/// Creates a bent block mesh by segmenting it along the bend axis and applying transformations.
	/// This replicates MineImator's model_shape_generate_block function.
	///
	/// The mesh is segmented along the bend axis. Each segment's vertices are transformed by
	/// a bend matrix that rotates them around the bend pivot point. The weight of the rotation
	/// increases through the bend zone, creating a smooth curve.
	/// </summary>
	private MeshInstance3D CreateBentBlockMesh(string partName, int shapeIndex, Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, float sizeZ, int texWidth, int texHeight,
		bool textureMirror, bool invert, float inflate, MiBend bend, Vector3 shapeScale)
	{
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var uvs = new List<Vector2>();
		var indices = new List<int>();

		// Parse bend configuration
		var config = BendHelper.ParseBendConfig(bend);
		if (config == null)
		{
			// Fall back to regular block mesh if bend config is invalid
			return CreateBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ, texWidth, texHeight, textureMirror, invert, inflate);
		}

		int segAxis = BendHelper.GetSegmentAxis(config.Part);
		float scalef = 0.005f; // Z-fighting prevention scale factor

		// Get bend offset and size in Godot units
		float bendOffsetGodot = config.Offset / 16.0f;
		float? bendSizeGodot = config.Size.HasValue ? config.Size.Value / 16.0f : null;

		// Get the default bend angles (these are baked into the mesh at load time)
		Vector3 bendAngles = new Vector3(config.DefaultAngle[0], config.DefaultAngle[1], config.DefaultAngle[2]);

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

		// Apply inflate
		if (inflate != 0.0f)
		{
			min -= new Vector3(inflate, inflate, inflate);
			max += new Vector3(inflate, inflate, inflate);
		}

		// Calculate size
		Vector3 size = max - min;
		float sizeAlongAxis = size[segAxis];

		// Calculate bend zone
		float shapeFromAlongAxis = min[segAxis];
		var (bendStart, bendEnd, actualBendSize) = BendHelper.GetBendZone(bendOffsetGodot, bendSizeGodot, shapeFromAlongAxis, realistic: true);

		// Determine number of segments
		float scaleOnAxis = shapeScale[segAxis];
		int detail = BendHelper.GetBendDetail(bendSizeGodot, realistic: true, scale: scaleOnAxis);
		float bendSegSize = actualBendSize / detail;

		// Check if the mesh is actually bent (non-zero angles on active axes)
		bool isBent = false;
		for (int i = 0; i < 3; i++)
		{
			if (config.Axis[i] && Math.Abs(bendAngles[i]) > 0.001f)
			{
				isBent = true;
				break;
			}
		}

		// If not bent, fall back to regular block mesh
		if (!isBent)
		{
			return CreateBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ, texWidth, texHeight, textureMirror, invert, inflate);
		}

		// Calculate UV coordinates (same as regular block)
		float texU = uvU / texWidth;
		float texV = uvV / texHeight;
		float texSizeXn = sizeX / texWidth;
		float texSizeYn = sizeY / texHeight;
		float texSizeZn = sizeZ / texHeight;
		float texSizeFixX = (sizeX - 1.0f / 256.0f) / texWidth;
		float texSizeFixY = (sizeY - 1.0f / 256.0f) / texHeight;
		float texSizeFixZ = (sizeZ - 1.0f / 256.0f) / texHeight;

		// Pre-calculate all face UVs (matching MineImator's block UV layout)
		var texSouth1 = new Vector2(texU, texV);
		var texSouth2 = new Vector2(texU + texSizeFixX, texV);
		var texSouth3 = new Vector2(texU + texSizeFixX, texV + texSizeFixZ);
		var texSouth4 = new Vector2(texU, texV + texSizeFixZ);

		var texEast1 = new Vector2(texU + texSizeXn, texV);
		var texEast2 = new Vector2(texU + texSizeXn + texSizeFixY, texV);
		var texEast3 = new Vector2(texU + texSizeXn + texSizeFixY, texV + texSizeFixZ);
		var texEast4 = new Vector2(texU + texSizeXn, texV + texSizeFixZ);

		var texWest1 = new Vector2(texU - texSizeYn, texV);
		var texWest2 = new Vector2(texU - texSizeYn + texSizeFixY, texV);
		var texWest3 = new Vector2(texU - texSizeYn + texSizeFixY, texV + texSizeFixZ);
		var texWest4 = new Vector2(texU - texSizeYn, texV + texSizeFixZ);

		var texNorth1 = new Vector2(texU + texSizeXn + texSizeYn, texV);
		var texNorth2 = new Vector2(texU + texSizeXn + texSizeYn + texSizeFixX, texV);
		var texNorth3 = new Vector2(texU + texSizeXn + texSizeYn + texSizeFixX, texV + texSizeFixZ);
		var texNorth4 = new Vector2(texU + texSizeXn + texSizeYn, texV + texSizeFixZ);

		var texUp1 = new Vector2(texU, texV - texSizeYn);
		var texUp2 = new Vector2(texU + texSizeFixX, texV - texSizeYn);
		var texUp3 = new Vector2(texU + texSizeFixX, texV - texSizeYn + texSizeFixY);
		var texUp4 = new Vector2(texU, texV - texSizeYn + texSizeFixY);

		var texDown4 = new Vector2(texU + texSizeXn, texV - texSizeYn);
		var texDown3 = new Vector2(texU + texSizeXn + texSizeFixX, texV - texSizeYn);
		var texDown2 = new Vector2(texU + texSizeXn + texSizeFixX, texV - texSizeYn + texSizeFixY);
		var texDown1 = new Vector2(texU + texSizeXn, texV - texSizeYn + texSizeFixY);

		// Apply texture mirror
		if (textureMirror)
		{
			(texEast1, texWest1) = (texWest1, texEast1);
			(texEast2, texWest2) = (texWest2, texEast2);
			(texEast3, texWest3) = (texWest3, texEast3);
			(texEast4, texWest4) = (texWest4, texEast4);
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

		// Set up initial points, start/end face UVs based on segment axis
		Vector3 p1, p2, p3, p4;
		Vector2 texStart1, texStart2, texStart3, texStart4;
		Vector2 texEnd1, texEnd2, texEnd3, texEnd4;

		if (segAxis == 0) // X axis (left/right bending)
		{
			p1 = new Vector3(min.X, min.Y, max.Z);
			p2 = new Vector3(min.X, max.Y, max.Z);
			p3 = new Vector3(min.X, max.Y, min.Z);
			p4 = new Vector3(min.X, min.Y, min.Z);
			texStart1 = texWest1; texStart2 = texWest2; texStart3 = texWest3; texStart4 = texWest4;
			texEnd1 = texEast1; texEnd2 = texEast2; texEnd3 = texEast3; texEnd4 = texEast4;
		}
		else if (segAxis == 1) // Y axis (front/back bending)
		{
			p1 = new Vector3(max.X, min.Y, max.Z);
			p2 = new Vector3(min.X, min.Y, max.Z);
			p3 = new Vector3(min.X, min.Y, min.Z);
			p4 = new Vector3(max.X, min.Y, min.Z);
			texStart1 = texNorth1; texStart2 = texNorth2; texStart3 = texNorth3; texStart4 = texNorth4;
			texEnd1 = texSouth1; texEnd2 = texSouth2; texEnd3 = texSouth3; texEnd4 = texSouth4;
		}
		else // Z axis (upper/lower bending)
		{
			p1 = new Vector3(min.X, max.Y, min.Z);
			p2 = new Vector3(max.X, max.Y, min.Z);
			p3 = new Vector3(max.X, min.Y, min.Z);
			p4 = new Vector3(min.X, min.Y, min.Z);
			texStart1 = texDown1; texStart2 = texDown2; texStart3 = texDown3; texStart4 = texDown4;
			texEnd1 = texUp1; texEnd2 = texUp2; texEnd3 = texUp3; texEnd4 = texUp4;
		}

		// Helper to compute the bend matrix for a given segment position
		Transform3D ComputeSegmentBendMatrix(float segPos)
		{
			float weight = BendHelper.CalculateBendWeight(segPos, bendStart, bendEnd, actualBendSize, config.Part);
			Vector3 easedBend = BendHelper.GetBendVector(bendAngles, weight);

			// Scale includes Z-fighting prevention: scale increases with weight
			Vector3 matScale = Vector3.One + new Vector3(weight * scalef, weight * scalef, weight * scalef);

			return BendHelper.GetShapeBendMatrix(config, easedBend, matScale);
		}

		// Apply initial bend transform to starting points
		Transform3D startMat = ComputeSegmentBendMatrix(0);
		p1 = startMat * p1;
		p2 = startMat * p2;
		p3 = startMat * p3;
		p4 = startMat * p4;

		// Generate segments
		float segPos = 0.0f;

		while (true)
		{
			// Check if we've reached the end
			if (segPos >= sizeAlongAxis - 0.0001f)
			{
				// Add end face
				int baseIdx = vertices.Count;
				vertices.Add(p1); vertices.Add(p2); vertices.Add(p3); vertices.Add(p4);

				Vector3 faceNormal = (p2 - p1).Cross(p4 - p1).Normalized();
				normals.Add(faceNormal); normals.Add(faceNormal); normals.Add(faceNormal); normals.Add(faceNormal);

				uvs.Add(texEnd1); uvs.Add(texEnd2); uvs.Add(texEnd3); uvs.Add(texEnd4);

				if (invert)
				{
					indices.Add(baseIdx + 2); indices.Add(baseIdx + 1); indices.Add(baseIdx + 0);
					indices.Add(baseIdx + 3); indices.Add(baseIdx + 2); indices.Add(baseIdx + 0);
				}
				else
				{
					indices.Add(baseIdx + 0); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
					indices.Add(baseIdx + 0); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
				}
				break;
			}

			// Add start face for first segment
			if (segPos < 0.001f)
			{
				int baseIdx = vertices.Count;
				vertices.Add(p1); vertices.Add(p2); vertices.Add(p3); vertices.Add(p4);

				Vector3 faceNormal = (p2 - p1).Cross(p4 - p1).Normalized();
				normals.Add(faceNormal); normals.Add(faceNormal); normals.Add(faceNormal); normals.Add(faceNormal);

				uvs.Add(texStart1); uvs.Add(texStart2); uvs.Add(texStart3); uvs.Add(texStart4);

				if (invert)
				{
					indices.Add(baseIdx + 0); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
					indices.Add(baseIdx + 0); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
				}
				else
				{
					indices.Add(baseIdx + 2); indices.Add(baseIdx + 1); indices.Add(baseIdx + 0);
					indices.Add(baseIdx + 3); indices.Add(baseIdx + 2); indices.Add(baseIdx + 0);
				}
			}

			// Calculate segment size (matching MineImator's logic)
			float segSize;
			if (segPos < bendStart)
			{
				segSize = Math.Min(sizeAlongAxis - segPos, bendStart - segPos);
			}
			else if (segPos >= bendEnd)
			{
				segSize = sizeAlongAxis - segPos;
			}
			else
			{
				segSize = bendSegSize;
				if (segPos == 0)
					segSize -= (shapeFromAlongAxis - bendStart) % bendSegSize;
				segSize = Math.Min(segSize, sizeAlongAxis - segPos);
			}
			segSize = Math.Max(segSize, 0.005f);

			// Advance position
			segPos += segSize;

			// Calculate next points (un-transformed)
			Vector3 np1, np2, np3, np4;

			if (segAxis == 0) // X axis
			{
				np1 = new Vector3(min.X + segPos, min.Y, max.Z);
				np2 = new Vector3(min.X + segPos, max.Y, max.Z);
				np3 = new Vector3(min.X + segPos, max.Y, min.Z);
				np4 = new Vector3(min.X + segPos, min.Y, min.Z);
			}
			else if (segAxis == 1) // Y axis
			{
				np1 = new Vector3(max.X, min.Y + segPos, max.Z);
				np2 = new Vector3(min.X, min.Y + segPos, max.Z);
				np3 = new Vector3(min.X, min.Y + segPos, min.Z);
				np4 = new Vector3(max.X, min.Y + segPos, min.Z);
			}
			else // Z axis
			{
				np1 = new Vector3(min.X, max.Y, min.Z + segPos);
				np2 = new Vector3(max.X, max.Y, min.Z + segPos);
				np3 = new Vector3(max.X, min.Y, min.Z + segPos);
				np4 = new Vector3(min.X, min.Y, min.Z + segPos);
			}

			// Apply bend transform to next points
			Transform3D nextMat = ComputeSegmentBendMatrix(segPos);
			np1 = nextMat * np1;
			np2 = nextMat * np2;
			np3 = nextMat * np3;
			np4 = nextMat * np4;

			// Add side faces between current and next points
			AddBentSideFaces(vertices, normals, uvs, indices, p1, p2, p3, p4, np1, np2, np3, np4,
				segPos / sizeAlongAxis, texSizeFixX, texSizeFixY, texSizeFixZ, segAxis, invert);

			// Update current points
			p1 = np1; p2 = np2; p3 = np3; p4 = np4;
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

		var meshInstance = new MeshInstance3D();
		meshInstance.Mesh = arrayMesh;

		return meshInstance;
	}
	
	/// <summary>
	/// Adds the 4 side faces between two segment cross-sections
	/// </summary>
	private void AddBentSideFaces(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
		Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, Vector3 np1, Vector3 np2, Vector3 np3, Vector3 np4,
		float texOffset, float texSizeX, float texSizeY, float texSizeZ, int segAxis, bool invert)
	{
		// Add 4 side faces
		// The faces depend on which axis we're segmenting along
		
		if (segAxis == 0) // X axis segmentation
		{
			// South face (Y+ side, toward max.Y)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				p2, np2, np3, p3,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeY),
				new Vector2(texOffset, texSizeY),
				invert);
			
			// North face (Y- side, toward min.Y)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				np1, p1, p4, np4,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeY),
				new Vector2(texOffset, texSizeY),
				invert);
			
			// Up face (Z+ side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				p1, np1, np2, p2,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeZ),
				new Vector2(texOffset, texSizeZ),
				invert);
			
			// Down face (Z- side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				np4, p4, p3, np3,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeZ),
				new Vector2(texOffset, texSizeZ),
				invert);
		}
		else if (segAxis == 1) // Y axis segmentation
		{
			// East face (X+ side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				p1, np1, np4, p4,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeZ),
				new Vector2(texOffset, texSizeZ),
				invert);
			
			// West face (X- side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				np2, p2, p3, np3,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeZ),
				new Vector2(texOffset, texSizeZ),
				invert);
			
			// Front face (Z+ side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				p2, np2, np1, p1,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeZ),
				new Vector2(texOffset, texSizeZ),
				invert);
			
			// Back face (Z- side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				np4, p4, p3, np3,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeZ),
				new Vector2(texOffset, texSizeZ),
				invert);
		}
		else // Z axis segmentation
		{
			// East face (X+ side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				p2, np2, np3, p3,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeY),
				new Vector2(texOffset, texSizeY),
				invert);
			
			// West face (X- side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				np1, p1, p4, np4,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeY),
				new Vector2(texOffset, texSizeY),
				invert);
			
			// North face (Y+ side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				p1, np1, np2, p2,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeY),
				new Vector2(texOffset, texSizeY),
				invert);
			
			// South face (Y- side)
			AddQuadWithCalculatedNormal(vertices, normals, uvs, indices,
				np4, p4, p3, np3,
				new Vector2(texOffset, 0),
				new Vector2(texOffset + texSizeX, 0),
				new Vector2(texOffset + texSizeX, texSizeY),
				new Vector2(texOffset, texSizeY),
				invert);
		}
	}
	
	/// <summary>
	/// Adds a quad with normal calculated from vertices
	/// </summary>
	private void AddQuadWithCalculatedNormal(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
		Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
		Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector2 uv4,
		bool invert)
	{
		int baseIdx = vertices.Count;
		
		vertices.Add(v1);
		vertices.Add(v2);
		vertices.Add(v3);
		vertices.Add(v4);
		
		// Calculate normal from cross product
		Vector3 edge1 = v2 - v1;
		Vector3 edge2 = v4 - v1;
		Vector3 normal = edge1.Cross(edge2).Normalized();
		
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
		
		uvs.Add(uv1);
		uvs.Add(uv2);
		uvs.Add(uv3);
		uvs.Add(uv4);
		
		if (invert)
		{
			indices.Add(baseIdx + 2);
			indices.Add(baseIdx + 1);
			indices.Add(baseIdx + 0);
			indices.Add(baseIdx + 3);
			indices.Add(baseIdx + 2);
			indices.Add(baseIdx + 0);
		}
		else
		{
			indices.Add(baseIdx + 0);
			indices.Add(baseIdx + 1);
			indices.Add(baseIdx + 2);
			indices.Add(baseIdx + 0);
			indices.Add(baseIdx + 2);
			indices.Add(baseIdx + 3);
		}
	}
	
	/// <summary>
	/// Creates a bent plane mesh
	/// </summary>
	private MeshInstance3D CreateBentPlaneMesh(Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, int texWidth, int texHeight, 
		bool textureMirror, bool invert, float inflate, MiBend bend, Vector3 shapeScale)
	{
		// For now, fall back to regular plane mesh
		// Bent plane implementation would be similar to bent block but with only front/back faces
		return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, textureMirror, invert, inflate);
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
		
		var meshInstance = new MeshInstance3D();
		meshInstance.Mesh = arrayMesh;
		
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
		
		var meshInstance = new MeshInstance3D();
		meshInstance.Mesh = arrayMesh;
		
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
	/// Adds a face to the mesh data with pre-calculated UV coordinates
	/// </summary>
	private void AddFaceWithUVs(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
		Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, Vector3 vertex3, Vector3 normal,
		Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, bool invert)
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
		
		GD.Print($"Attempting to load Mine Imator texture from: {texturePath}");
		
		if (!File.Exists(texturePath))
		{
			GD.PrintErr($"Mine Imator texture not found: {texturePath}");
			return null;
		}
		
		try
		{
			// Read file bytes using System.IO (works for external paths)
			byte[] fileBytes = File.ReadAllBytes(texturePath);
			
			GD.Print($"Read {fileBytes.Length} bytes from texture file");
			
			// Check for PNG header
			if (fileBytes.Length >= 8)
			{
				bool isPng = fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && 
				             fileBytes[2] == 0x4E && fileBytes[3] == 0x47;
				GD.Print($"PNG header check: {(isPng ? "Valid PNG header" : "Not a standard PNG header")}");
				GD.Print($"First 16 bytes: {BitConverter.ToString(fileBytes, 0, Math.Min(16, fileBytes.Length))}");
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
			
			if (error != Error.Ok)
			{
				GD.PrintErr($"Failed to load Mine Imator texture: {texturePath} (Error: {error}, Format: {extension})");
				
				// Try loading with Godot's built-in image loader as fallback
				GD.Print("Attempting fallback: copying to temp file and using Image.Load()");
				string tempPath = Path.Combine(Path.GetTempPath(), $"mi_texture_{Guid.NewGuid()}{extension}");
				try
				{
					File.Copy(texturePath, tempPath, true);
					var fallbackImage = new Image();
					var fallbackError = fallbackImage.Load(tempPath);
					File.Delete(tempPath);
					
					if (fallbackError == Error.Ok && !fallbackImage.IsEmpty())
					{
						GD.Print($"Fallback succeeded! Loaded texture: {model.Texture} ({fallbackImage.GetWidth()}x{fallbackImage.GetHeight()})");
						return ImageTexture.CreateFromImage(fallbackImage);
					}
				}
				catch (Exception fallbackEx)
				{
					GD.PrintErr($"Fallback also failed: {fallbackEx.Message}");
				}
				
				return null;
			}
			
			if (image.IsEmpty())
			{
				GD.PrintErr($"Mine Imator texture loaded but image is empty: {texturePath}");
				return null;
			}
			
			var texture = ImageTexture.CreateFromImage(image);
			GD.Print($"Loaded Mine Imator texture: {model.Texture} ({image.GetWidth()}x{image.GetHeight()})");
			
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
	
	[JsonPropertyName("invert")]
	[JsonConverter(typeof(BoolOrArrayConverter))]
	public bool[] Invert { get; set; } // Can be single bool or array of bools
	
	[JsonPropertyName("angle")]
	[JsonConverter(typeof(SingleOrArrayConverter))]
	public float[] Angle { get; set; } // Can be single float or array of floats (default angle)
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
public class BoolOrArrayConverter : JsonConverter<bool[]>
{
	public override bool[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.True)
		{
			return new bool[] { true };
		}
		else if (reader.TokenType == JsonTokenType.False)
		{
			return new bool[] { false };
		}
		else if (reader.TokenType == JsonTokenType.StartArray)
		{
			var list = new List<bool>();
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndArray)
					break;
				if (reader.TokenType == JsonTokenType.True)
					list.Add(true);
				else if (reader.TokenType == JsonTokenType.False)
					list.Add(false);
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
			{
				writer.WriteBooleanValue(v);
			}
			writer.WriteEndArray();
		}
	}
}

#endregion
