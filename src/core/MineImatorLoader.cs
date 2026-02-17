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
		
		// Flatten parts and create bones
		var boneDataList = new List<(MiPart part, int boneIdx, int parentIdx)>();
		FlattenPartsForBones(model.Parts, -1, boneDataList);
		
		// Create all bones first
		foreach (var (part, boneIdx, parentIdx) in boneDataList)
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
		foreach (var (part, boneIdx, parentIdx) in boneDataList)
		{
			if (part.Shapes != null && part.Shapes.Count > 0)
			{
				string boneName = skeleton.GetBoneName(boneIdx);
				if (character.BoneObjects.TryGetValue(boneName, out var boneObject))
				{
					int shapeIndex = 0;
					foreach (var shape in part.Shapes)
					{
						var meshInstance = CreateShapeMesh(part.Name, shapeIndex, shape, model, texture);
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
		
		GD.Print($"Created Mine Imator character: {model.Name} with {skeleton.GetBoneCount()} bones");
		
		return character;
	}
	
	/// <summary>
	/// Flattens the part hierarchy for bone creation
	/// </summary>
	private void FlattenPartsForBones(List<MiPart> parts, int parentIdx, 
		List<(MiPart part, int boneIdx, int parentIdx)> boneDataList)
	{
		if (parts == null) return;
		
		foreach (var part in parts)
		{
			int currentIdx = boneDataList.Count;
			boneDataList.Add((part, currentIdx, parentIdx));
			
			if (part.Parts != null && part.Parts.Count > 0)
			{
				FlattenPartsForBones(part.Parts, currentIdx, boneDataList);
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
		var restTransform = Transform3D.Identity
			.RotatedLocal(Vector3.Right, rotation.X)
			.RotatedLocal(Vector3.Up, rotation.Y)
			.RotatedLocal(Vector3.Forward, rotation.Z)
			.ScaledLocal(scale)
			.Translated(position);
		
		skeleton.SetBoneRest(addedIdx, restTransform);
		skeleton.SetBonePosePosition(addedIdx, position);
		skeleton.SetBonePoseRotation(addedIdx, Quaternion.FromEuler(rotation));
		skeleton.SetBonePoseScale(addedIdx, scale);
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
	private MeshInstance3D CreateShapeMesh(string partName, int shapeIndex, MiShape shape, MiModel model, ImageTexture texture)
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
		
		// Apply shape scale if present
		Vector3 shapeScale = Vector3.One;
		if (shape.Scale != null && shape.Scale.Length >= 3)
		{
			shapeScale = new Vector3(shape.Scale[0], shape.Scale[1], shape.Scale[2]);
		}
		
		// Get inflate value and scale it from Minecraft pixels to Godot units (divide by 16)
		float inflate = shape.Inflate / 16.0f;
		
		MeshInstance3D meshInstance;
		
		if (shape.Type == "plane")
		{
			// Treat 3D planes like items - as extruded planes with per-pixel extrusion
			if (shape.ThreeD)
			{
				// Create an extruded item-like plane with per-pixel hull mesh
				meshInstance = CreateExtrudedPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, texture, shape.TextureMirror, shape.Invert, inflate);
			}
			else
			{
				// Regular 2D plane
				meshInstance = CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate);
			}
		}
		else // "block" or default
		{
			meshInstance = CreateBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ, texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate);
		}
		
		// Apply shape scale to the mesh instance
		if (meshInstance != null)
		{
			meshInstance.Position = shapePosition;
			meshInstance.Rotation = shapeRotation;
			meshInstance.Scale = shapeScale;
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
		
		// Mirror texture on X if needed
		if (textureMirror)
		{
			// Switch left/right points
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
		uvs.Add(tex4);
		uvs.Add(tex3);
		uvs.Add(tex2);
		uvs.Add(tex1);
		
		if (invert)
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 3);
		}
		else
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 3);
			indices.Add(baseVertex + 2);
		}
		
		// Back face (at max.Z, normal pointing forward) - for two-sided rendering
		baseVertex = vertices.Count;
		
		// Use the same vertices but in reverse order for the back face
		vertices.Add(new Vector3(min.X, min.Y, max.Z));
		vertices.Add(new Vector3(max.X, min.Y, max.Z));
		vertices.Add(new Vector3(max.X, max.Y, max.Z));
		vertices.Add(new Vector3(min.X, max.Y, max.Z));
		
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		
		// UV mapping for back face (same orientation)
		uvs.Add(tex4);
		uvs.Add(tex3);
		uvs.Add(tex2);
		uvs.Add(tex1);
		
		// Back face indices - reverse winding from front face
		if (invert)
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 3);
			indices.Add(baseVertex + 2);
		}
		else
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 3);
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
					float posX = from.X + px * pixelScaleX;
					float posY = from.Y + py * pixelScaleY;
					
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
					float centerZ = from.Z; // Plane is at Z=0 by default
					
					// UV coordinates for this pixel (normalized)
					float uvX = (texX + 0.5f) / texWidth;
					float uvY = (texY + 0.5f) / texHeight;
					
					if (textureMirror)
					{
						uvX = 1.0f - uvX;
					}
					
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
					bool leftEmpty = px == 0 || image.GetPixel(uvStartX + px - 1, texY).A <= 0.5f;
					bool rightEmpty = px == regionWidth - 1 || image.GetPixel(uvStartX + px + 1, texY).A <= 0.5f;
					bool topEmpty = py == 0 || image.GetPixel(texX, uvStartY + py - 1).A <= 0.5f;
					bool bottomEmpty = py == regionHeight - 1 || image.GetPixel(texX, uvStartY + py + 1).A <= 0.5f;
					
					if (leftEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX, posY, centerZ - halfThickness),
							new Vector3(posX, posY, centerZ + halfThickness),
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							Vector3.Left, uvX, uvY, invert);
					}
					
					if (rightEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ + halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							Vector3.Right, uvX, uvY, invert);
					}
					
					if (topEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX, posY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ - halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY, centerZ + halfThickness),
							new Vector3(posX, posY, centerZ + halfThickness),
							Vector3.Down, uvX, uvY, invert);
					}
					
					if (bottomEmpty)
					{
						baseVertex = vertices.Count;
						AddExtrudedQuad(vertices, normals, uvs, indices, baseVertex,
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ + halfThickness),
							new Vector3(posX + adjustedPixelScaleX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							new Vector3(posX, posY + adjustedPixelScaleY, centerZ - halfThickness),
							Vector3.Up, uvX, uvY, invert);
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
		uvs.Add(new Vector2(uvX, uvY));
		uvs.Add(new Vector2(uvX, uvY));
		uvs.Add(new Vector2(uvX, uvY));
		uvs.Add(new Vector2(uvX, uvY));
		
		if (invert)
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 3);
		}
		else
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 3);
			indices.Add(baseVertex + 2);
		}
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
		
		uvs.Add(new Vector2(u0, v1));
		uvs.Add(new Vector2(u1, v1));
		uvs.Add(new Vector2(u1, v0));
		uvs.Add(new Vector2(u0, v0));
		
		if (invert)
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 3);
		}
		else
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 3);
			indices.Add(baseVertex + 2);
		}
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
		uvs.Add(uv0);
		uvs.Add(uv1);
		uvs.Add(uv2);
		uvs.Add(uv3);
		
		if (invert)
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 3);
		}
		else
		{
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 2);
			indices.Add(baseVertex + 1);
			indices.Add(baseVertex + 0);
			indices.Add(baseVertex + 3);
			indices.Add(baseVertex + 2);
		}
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

#endregion
