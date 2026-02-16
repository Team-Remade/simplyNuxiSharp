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
				PropertyNameCaseInsensitive = true
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
					foreach (var shape in part.Shapes)
					{
						var meshInstance = CreateShapeMesh(shape, model, texture);
						if (meshInstance != null)
						{
							// Add the mesh as a visual child of the BoneSceneObject
							boneObject.AddVisualInstance(meshInstance);
							meshInstance.Name = $"{part.Name}_Shape";
						}
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
				Mathf.DegToRad(part.Rotation[2]) 
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
	private MeshInstance3D CreateShapeMesh(MiShape shape, MiModel model, ImageTexture texture)
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
		
		// Apply shape scale if present
		Vector3 shapeScale = Vector3.One;
		if (shape.Scale != null && shape.Scale.Length >= 3)
		{
			shapeScale = new Vector3(shape.Scale[0], shape.Scale[1], shape.Scale[2]);
		}
		
		MeshInstance3D meshInstance;
		
		if (shape.Type == "plane")
		{
			meshInstance = CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight, shape.TextureMirror, shape.Invert);
		}
		else // "block" or default
		{
			meshInstance = CreateBlockMesh(from, to, uvU, uvV, sizeX, sizeY, sizeZ, texWidth, texHeight, shape.TextureMirror, shape.Invert);
		}
		
		// Apply shape scale to the mesh instance
		if (meshInstance != null && shapeScale != Vector3.One)
		{
			meshInstance.Scale = shapeScale;
		}
		
		// Apply material with texture
		if (meshInstance != null && texture != null)
		{
			var material = new StandardMaterial3D();
			material.AlbedoTexture = texture;
			material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
			material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
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
	private MeshInstance3D CreateBlockMesh(Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, float sizeZ, int texWidth, int texHeight, 
		bool textureMirror, bool invert)
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
		
		// Create all 6 faces
		// Front face (Z+)
		AddFace(vertices, normals, uvs, indices,
			new Vector3(min.X, min.Y, max.Z),
			new Vector3(max.X, min.Y, max.Z),
			new Vector3(max.X, max.Y, max.Z),
			new Vector3(min.X, max.Y, max.Z),
			Vector3.Back,
			uvU, uvV,
			sizeX, sizeY,
			texWidth, texHeight,
			textureMirror, invert);
		
		// Right face (X+): left of starting square (u - depth, v)
		AddFace(vertices, normals, uvs, indices,
			new Vector3(max.X, min.Y, max.Z),
			new Vector3(max.X, min.Y, min.Z),
			new Vector3(max.X, max.Y, min.Z),
			new Vector3(max.X, max.Y, max.Z),
			Vector3.Right,
			uvU - sizeZ, uvV,
			sizeZ, sizeY,
			texWidth, texHeight,
			textureMirror, invert);
		
		// Left face (X-): right of starting square (u + width, v)
		AddFace(vertices, normals, uvs, indices,
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(min.X, min.Y, max.Z),
			new Vector3(min.X, max.Y, max.Z),
			new Vector3(min.X, max.Y, min.Z),
			Vector3.Left,
			uvU + sizeX, uvV,
			sizeZ, sizeY,
			texWidth, texHeight,
			textureMirror, invert);
		
		// Back face (Z-): right of left square (u + width + depth, v)
		AddFace(vertices, normals, uvs, indices,
			new Vector3(max.X, min.Y, min.Z),
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(min.X, max.Y, min.Z),
			new Vector3(max.X, max.Y, min.Z),
			Vector3.Forward,
			uvU + sizeX + sizeZ, uvV,
			sizeX, sizeY,
			texWidth, texHeight,
			textureMirror, invert);
		
		// Top face (Y+): top of starting square (u, v + height)
		AddFace(vertices, normals, uvs, indices,
			new Vector3(min.X, max.Y, max.Z),
			new Vector3(max.X, max.Y, max.Z),
			new Vector3(max.X, max.Y, min.Z),
			new Vector3(min.X, max.Y, min.Z),
			Vector3.Up,
			uvU, uvV + sizeY,
			sizeX, sizeZ,
			texWidth, texHeight,
			textureMirror, invert);
		
		// Bottom face (Y-): right of top square (u + width, v + height)
		AddFace(vertices, normals, uvs, indices,
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(max.X, min.Y, min.Z),
			new Vector3(max.X, min.Y, max.Z),
			new Vector3(min.X, min.Y, max.Z),
			Vector3.Down,
			uvU + sizeX, uvV + sizeY,
			sizeX, sizeZ,
			texWidth, texHeight,
			textureMirror, invert);
		
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
	/// Creates a plane mesh (single face)
	/// </summary>
	private MeshInstance3D CreatePlaneMesh(Vector3 from, Vector3 to, float uvU, float uvV,
		float sizeX, float sizeY, int texWidth, int texHeight, 
		bool textureMirror, bool invert)
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
		
		// UV coordinates (convert pixel coordinates to 0-1 range)
		float u0 = uvU / texWidth;
		float v0 = uvV / texHeight;
		float u1 = (uvU + sizeX) / texWidth;
		float v1 = (uvV + sizeY) / texHeight;
		
		if (textureMirror)
		{
			(u0, u1) = (u1, u0);
		}
		
		int baseVertex = vertices.Count;
		
		vertices.Add(new Vector3(min.X, min.Y, min.Z));
		vertices.Add(new Vector3(max.X, min.Y, min.Z));
		vertices.Add(new Vector3(max.X, max.Y, min.Z));
		vertices.Add(new Vector3(min.X, max.Y, min.Z));
		
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		normals.Add(Vector3.Forward);
		
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
		
		var image = Image.LoadFromFile(texturePath);
		if (image == null)
		{
			GD.PrintErr($"Failed to load Mine Imator texture: {texturePath}");
			return null;
		}
		
		var texture = ImageTexture.CreateFromImage(image);
		GD.Print($"Loaded Mine Imator texture: {model.Texture} ({image.GetWidth()}x{image.GetHeight()})");
		
		return texture;
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
	
	[JsonPropertyName("position")]
	public float[] Position { get; set; }
	
	[JsonPropertyName("rotation")]
	public float[] Rotation { get; set; }
	
	[JsonPropertyName("scale")]
	public float[] Scale { get; set; }
	
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
	
	[JsonPropertyName("scale")]
	public float[] Scale { get; set; }
	
	[JsonPropertyName("invert")]
	public bool Invert { get; set; }
	
	[JsonPropertyName("texture_mirror")]
	public bool TextureMirror { get; set; }
}

#endregion
