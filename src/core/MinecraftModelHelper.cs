using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Helper class for working with loaded Minecraft models
/// </summary>
public static class MinecraftModelHelper
{
	/// <summary>
	/// Gets a model by name, searching through common path patterns
	/// </summary>
	public static MinecraftModel GetModelByName(string name)
	{
		var loader = MinecraftJsonLoader.Instance;
		
		if (!loader.IsLoaded)
		{
			GD.PrintErr("Cannot get model - Minecraft JSON files are not loaded yet!");
			return null;
		}
		
		// Try direct path first
		if (loader.HasModel(name))
		{
			return loader.GetModel(name);
		}
		
		// Try common patterns
		var patterns = new[]
		{
			$"models/block/{name}.json",
			$"models/item/{name}.json",
			$"block/{name}.json",
			$"item/{name}.json",
			$"{name}.json"
		};
		
		foreach (var pattern in patterns)
		{
			if (loader.HasModel(pattern))
			{
				return loader.GetModel(pattern);
			}
		}
		
		// Try fuzzy search
		var allPaths = loader.GetAllModelPaths();
		var matchingPath = allPaths.FirstOrDefault(p => 
			p.Contains(name, StringComparison.OrdinalIgnoreCase));
		
		if (matchingPath != null)
		{
			return loader.GetModel(matchingPath);
		}
		
		GD.PrintErr($"Model not found: {name}");
		return null;
	}
	
	/// <summary>
	/// Gets a blockstate by name, searching through common path patterns
	/// </summary>
	public static BlockState GetBlockStateByName(string name)
	{
		var loader = MinecraftJsonLoader.Instance;
		
		if (!loader.IsLoaded)
		{
			GD.PrintErr("Cannot get blockstate - Minecraft JSON files are not loaded yet!");
			return null;
		}
		
		// Try direct path first
		if (loader.HasBlockState(name))
		{
			return loader.GetBlockState(name);
		}
		
		// Try common patterns
		var patterns = new[]
		{
			$"blockstates/{name}.json",
			$"{name}.json"
		};
		
		foreach (var pattern in patterns)
		{
			if (loader.HasBlockState(pattern))
			{
				return loader.GetBlockState(pattern);
			}
		}
		
		// Try fuzzy search
		var allPaths = loader.GetAllBlockStatePaths();
		var matchingPath = allPaths.FirstOrDefault(p => 
			p.Contains(name, StringComparison.OrdinalIgnoreCase));
		
		if (matchingPath != null)
		{
			return loader.GetBlockState(matchingPath);
		}
		
		GD.PrintErr($"BlockState not found: {name}");
		return null;
	}
	
	/// <summary>
	/// Converts a Minecraft model element to a Godot MeshInstance3D with textures
	/// </summary>
	public static MeshInstance3D CreateMeshFromElement(ModelElement element, Dictionary<string, string> textures, Vector3 pivotOffset, Basis rotationBasis = default, bool hasRotation = false)
	{
		if (element == null || element.From == null || element.To == null)
		{
			return null;
		}
		
		var meshInstance = new MeshInstance3D();
		
		// Calculate size and position
		// Minecraft uses 16x16x16 grid, convert to Godot units
		float scale = 1.0f / 16.0f;
		
		var from = new Vector3(element.From[0], element.From[1], element.From[2]) * scale;
		var to = new Vector3(element.To[0], element.To[1], element.To[2]) * scale;
		
		var center = (from + to) / 2.0f;
		
		// Create a custom mesh with proper UV mapping and materials
		var arrayMesh = CreateCubeMeshWithTextures(from, to, element.Faces, textures, rotationBasis, hasRotation);
		
		meshInstance.Mesh = arrayMesh;
		
		// Calculate position
		var position = center + pivotOffset;
		
		// If we have rotation, transform the position
		if (hasRotation)
		{
			position = rotationBasis * position;
		}
		
		meshInstance.Position = position;
		
		// Apply rotation if present (element-level rotation)
		if (element.Rotation != null)
		{
			var rotation = element.Rotation;
			var origin = new Vector3(rotation.Origin[0], rotation.Origin[1], rotation.Origin[2]) * scale;
			var angle = Mathf.DegToRad(rotation.Angle);
			
			// Apply rotation based on axis
			switch (rotation.Axis?.ToLower())
			{
				case "x":
					meshInstance.RotateX(angle);
					break;
				case "y":
					meshInstance.RotateY(angle);
					break;
				case "z":
					meshInstance.RotateZ(angle);
					break;
			}
		}
		
		return meshInstance;
	}
	
	/// <summary>
	/// Creates a cube mesh with proper UV mapping for textures
	/// Each face can have its own texture via separate surfaces
	/// </summary>
	private static ArrayMesh CreateCubeMeshWithTextures(Vector3 from, Vector3 to, Dictionary<string, ElementFace> faces, Dictionary<string, string> textures, Basis rotationBasis = default, bool hasRotation = false)
	{
		// The mesh vertices are created centered around origin (0,0,0)
		// The caller will position the mesh at the correct location
		var size = to - from;
		var halfSize = size / 2.0f;
		
		// Calculate UV coordinates from face data (normalized 0-1 range)
		Vector2[] GetFaceUVs(ElementFace face)
		{
			if (face?.UV != null && face.UV.Length == 4)
			{
				// Minecraft UV is in 0-16 range, convert to 0-1
				float u1 = face.UV[0] / 16.0f;
				float v1 = face.UV[1] / 16.0f;
				float u2 = face.UV[2] / 16.0f;
				float v2 = face.UV[3] / 16.0f;
				
				return new Vector2[]
				{
					new Vector2(u1, v2),  // bottom-left
					new Vector2(u1, v1),  // top-left
					new Vector2(u2, v1),  // top-right
					new Vector2(u2, v2)   // bottom-right
				};
			}
			// Default UV covering full texture
			return new Vector2[]
			{
				new Vector2(0, 1),  // bottom-left
				new Vector2(0, 0),  // top-left
				new Vector2(1, 0),  // top-right
				new Vector2(1, 1)   // bottom-right
			};
		}
		
		// Create material for a face
		StandardMaterial3D CreateMaterial(string texturePath)
		{
			var material = new StandardMaterial3D();
			
			if (!string.IsNullOrEmpty(texturePath))
			{
				var texture = LoadMinecraftTexture(texturePath);
				if (texture != null)
				{
					material.AlbedoColor = Colors.White;
					material.AlbedoTexture = texture;
					material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
					material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
					material.AlphaScissorThreshold = 0.5f;
					return material;
				}
			}
			
			// Fallback material
			material.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
			return material;
		}
		
		// Helper to add a surface for a single face
		void AddFaceSurface(ArrayMesh arrayMesh, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
		                    Vector3 normal, Vector2[] faceUVs, string faceName)
		{
			// Apply rotation to vertices and normals if needed
			if (hasRotation)
			{
				v0 = rotationBasis * v0;
				v1 = rotationBasis * v1;
				v2 = rotationBasis * v2;
				v3 = rotationBasis * v3;
				normal = rotationBasis * normal;
			}
			
			var vertices = new List<Vector3> { v0, v1, v2, v3 };
			var uvs = new List<Vector2> { faceUVs[0], faceUVs[1], faceUVs[2], faceUVs[3] };
			var normals = new List<Vector3> { normal, normal, normal, normal };
			var indices = new List<int> { 0, 2, 1, 0, 3, 2 };
			
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
			arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
			arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
			
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			
			// Apply material to this surface
			if (faces != null && faces.ContainsKey(faceName))
			{
				var face = faces[faceName];
				if (face?.Texture != null)
				{
					var texturePath = ResolveTexturePath(face.Texture, textures);
					var material = CreateMaterial(texturePath);
					arrayMesh.SurfaceSetMaterial(arrayMesh.GetSurfaceCount() - 1, material);
				}
			}
		}
		
		var arrayMesh = new ArrayMesh();
		
		// Down face (y-) - Bottom
		if (faces != null && faces.ContainsKey("down"))
		{
			var faceUVs = GetFaceUVs(faces["down"]);
			AddFaceSurface(arrayMesh,
				new Vector3(halfSize.X, -halfSize.Y, -halfSize.Z),
				new Vector3(halfSize.X, -halfSize.Y, halfSize.Z),
				new Vector3(-halfSize.X, -halfSize.Y, halfSize.Z),
				new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),
				new Vector3(0, -1, 0), faceUVs, "down");
		}
		
		// Up face (y+) - Top
		if (faces != null && faces.ContainsKey("up"))
		{
			var faceUVs = GetFaceUVs(faces["up"]);
			AddFaceSurface(arrayMesh,
				new Vector3(-halfSize.X, halfSize.Y, -halfSize.Z),
				new Vector3(-halfSize.X, halfSize.Y, halfSize.Z),
				new Vector3(halfSize.X, halfSize.Y, halfSize.Z),
				new Vector3(halfSize.X, halfSize.Y, -halfSize.Z),
				new Vector3(0, 1, 0), faceUVs, "up");
		}
		
		// North face (z-)
		if (faces != null && faces.ContainsKey("north"))
		{
			var faceUVs = GetFaceUVs(faces["north"]);
			AddFaceSurface(arrayMesh,
				new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),
				new Vector3(-halfSize.X, halfSize.Y, -halfSize.Z),
				new Vector3(halfSize.X, halfSize.Y, -halfSize.Z),
				new Vector3(halfSize.X, -halfSize.Y, -halfSize.Z),
				new Vector3(0, 0, -1), faceUVs, "north");
		}
		
		// South face (z+)
		if (faces != null && faces.ContainsKey("south"))
		{
			var faceUVs = GetFaceUVs(faces["south"]);
			AddFaceSurface(arrayMesh,
				new Vector3(halfSize.X, -halfSize.Y, halfSize.Z),
				new Vector3(halfSize.X, halfSize.Y, halfSize.Z),
				new Vector3(-halfSize.X, halfSize.Y, halfSize.Z),
				new Vector3(-halfSize.X, -halfSize.Y, halfSize.Z),
				new Vector3(0, 0, 1), faceUVs, "south");
		}
		
		// West face (x-)
		if (faces != null && faces.ContainsKey("west"))
		{
			var faceUVs = GetFaceUVs(faces["west"]);
			AddFaceSurface(arrayMesh,
				new Vector3(-halfSize.X, -halfSize.Y, halfSize.Z),
				new Vector3(-halfSize.X, halfSize.Y, halfSize.Z),
				new Vector3(-halfSize.X, halfSize.Y, -halfSize.Z),
				new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),
				new Vector3(-1, 0, 0), faceUVs, "west");
		}
		
		// East face (x+)
		if (faces != null && faces.ContainsKey("east"))
		{
			var faceUVs = GetFaceUVs(faces["east"]);
			AddFaceSurface(arrayMesh,
				new Vector3(halfSize.X, -halfSize.Y, -halfSize.Z),
				new Vector3(halfSize.X, halfSize.Y, -halfSize.Z),
				new Vector3(halfSize.X, halfSize.Y, halfSize.Z),
				new Vector3(halfSize.X, -halfSize.Y, halfSize.Z),
				new Vector3(1, 0, 0), faceUVs, "east");
		}
		
		return arrayMesh;
	}
	
	/// <summary>
	/// Creates a complete 3D node from a Minecraft model with parent resolution
	/// </summary>
	public static Node3D CreateNodeFromModel(MinecraftModel model, float xRotation = 0, float yRotation = 0)
	{
		if (model == null)
		{
			return null;
		}
		
		GD.Print($"CreateNodeFromModel: Starting with model (parent: {model.Parent ?? "none"}), rotations X={xRotation}, Y={yRotation}");
		
		// Resolve the complete model with parents
		var resolvedModel = ResolveModelParents(model);
		
		if (resolvedModel == null)
		{
			GD.PrintErr("Failed to resolve model parents");
			return null;
		}
		
		if (resolvedModel.Elements == null || resolvedModel.Elements.Count == 0)
		{
			GD.PrintErr($"Model has no elements after parent resolution. Parent chain might be incomplete.");
			GD.PrintErr($"Resolved model info: parent={model.Parent}, textures={resolvedModel.Textures?.Count ?? 0}");
			return null;
		}
		
		GD.Print($"Successfully resolved model with {resolvedModel.Elements.Count} elements");
		
		float scale = 1.0f / 16.0f;
		
		// Calculate the bounding box of all elements to find the model bounds
		float minY = float.MaxValue;
		float maxY = float.MinValue;
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		float minZ = float.MaxValue;
		float maxZ = float.MinValue;
		
		foreach (var element in resolvedModel.Elements)
		{
			if (element.From != null && element.To != null)
			{
				minX = Mathf.Min(minX, element.From[0]);
				maxX = Mathf.Max(maxX, element.To[0]);
				minY = Mathf.Min(minY, element.From[1]);
				maxY = Mathf.Max(maxY, element.To[1]);
				minZ = Mathf.Min(minZ, element.From[2]);
				maxZ = Mathf.Max(maxZ, element.To[2]);
			}
		}
		
		GD.Print($"Model bounds: X[{minX}, {maxX}], Y[{minY}, {maxY}], Z[{minZ}, {maxZ}]");
		
		// Calculate the pivot offset to center the model at [8, 0, 8] in Minecraft coords
		// This ensures all blocks have a consistent pivot point at the bottom center
		Vector3 mcPivot = new Vector3(8, 0, 8);
		Vector3 mcCenter = new Vector3((minX + maxX) / 2.0f, minY, (minZ + maxZ) / 2.0f);
		Vector3 pivotOffset = (mcPivot - mcCenter) * scale;
		
		GD.Print($"MC Center: {mcCenter}, MC Pivot: {mcPivot}, Pivot Offset: {pivotOffset}");
		
		var root = new Node3D();
		
		// Build rotation transform if needed
		Basis rotationBasis = Basis.Identity;
		if (yRotation != 0 || xRotation != 0)
		{
			GD.Print($"Creating blockstate rotation basis X={xRotation}, Y={yRotation}");
			
			// Apply rotations in the correct order (Y then X, matching Minecraft)
			if (yRotation != 0)
			{
				rotationBasis = rotationBasis.Rotated(Vector3.Up, Mathf.DegToRad(yRotation));
			}
			if (xRotation != 0)
			{
				rotationBasis = rotationBasis.Rotated(Vector3.Right, Mathf.DegToRad(xRotation));
			}
		}
		
		// Create meshes for each element
		foreach (var element in resolvedModel.Elements)
		{
			var mesh = CreateMeshFromElement(element, resolvedModel.Textures, pivotOffset, rotationBasis, xRotation != 0 || yRotation != 0);
			if (mesh != null)
			{
				// Apply textures to the mesh
				ApplyTexturesToMesh(mesh, element.Faces, resolvedModel.Textures);
				root.AddChild(mesh);
			}
		}
		
		return root;
	}
	
	/// <summary>
	/// Loads and applies textures to a mesh based on face definitions
	/// </summary>
	private static void ApplyTexturesToMesh(MeshInstance3D meshInstance, Dictionary<string, ElementFace> faces, Dictionary<string, string> textures)
	{
		if (faces == null || textures == null || meshInstance.Mesh == null)
			return;
		
		// The mesh already has materials applied per surface in CreateCubeMeshWithTextures
		// This method is now a no-op but kept for backwards compatibility
		// Materials are applied during mesh creation to support different textures per face
	}
	
	/// <summary>
	/// Resolves a texture reference ( #varname or direct path) to an actual texture path
	/// </summary>
	private static string ResolveTexturePath(string textureRef, Dictionary<string, string> textures)
	{
		if (string.IsNullOrEmpty(textureRef))
			return null;
		
		// If it's a variable reference (#varname), resolve it
		if (textureRef.StartsWith("#"))
		{
			var varName = textureRef.Substring(1);
			if (textures != null && textures.ContainsKey(varName))
			{
				return textures[varName];
			}
			GD.PrintErr($"Texture variable not found: {textureRef}");
			return null;
		}
		
		// It's a direct path
		return textureRef;
	}
	
	/// <summary>
	/// Loads a Minecraft texture from the asset path
	/// </summary>
	private static Texture2D LoadMinecraftTexture(string texturePath)
	{
		if (string.IsNullOrEmpty(texturePath))
			return null;
		
		// Extract namespace if present (e.g., "farmersdelight:block/stove" -> namespace="farmersdelight", path="block/stove")
		string namespaceName = "minecraft"; // default namespace
		string actualPath = texturePath;
		
		if (texturePath.Contains(":"))
		{
			var parts = texturePath.Split(':', 2);
			namespaceName = parts[0];
			actualPath = parts[1];
		}
		
		// Try using the texture loader first (which has all textures cached by namespace)
		var textureLoader = MinecraftTextureLoader.Instance;
		if (textureLoader.IsLoaded)
		{
			// Build the key as stored in the texture loader: "namespace/path.png"
			var textureKey = $"{namespaceName}/{actualPath}.png";
			var cachedTexture = textureLoader.GetTexture(textureKey);
			if (cachedTexture != null)
			{
				GD.Print($"  ✓ Loaded texture from cache: {textureKey}");
				return cachedTexture;
			}
		}
		
		// Fallback: Try to load from file system directly
		var userDataPath = OS.GetUserDataDir();
		var dataPath = System.IO.Path.Combine(userDataPath, "data");
		
		// Search in multiple asset folders
		var assetFolders = new List<string>();
		if (System.IO.Directory.Exists(dataPath))
		{
			assetFolders.AddRange(System.IO.Directory.GetDirectories(dataPath)
				.Where(f => System.IO.Path.GetFileName(f).Contains("Assets", StringComparison.OrdinalIgnoreCase)));
		}
		
		foreach (var assetFolder in assetFolders)
		{
			var possiblePaths = new List<string>
			{
				System.IO.Path.Combine(assetFolder, "assets", namespaceName, "textures", actualPath + ".png"),
				System.IO.Path.Combine(assetFolder, "assets", namespaceName, "textures", actualPath.Replace("/", "\\") + ".png"),
			};
			
			foreach (var path in possiblePaths)
			{
				if (System.IO.File.Exists(path))
				{
					try
					{
						var image = Image.LoadFromFile(path);
						if (image != null)
						{
							var texture = ImageTexture.CreateFromImage(image);
							GD.Print($"  ✓ Loaded texture from file: {path}");
							return texture;
						}
					}
					catch (Exception ex)
					{
						GD.PrintErr($"Error loading texture {path}: {ex.Message}");
					}
				}
			}
		}
		
		GD.PrintErr($"  ✗ Texture not found: {texturePath}");
		return null;
	}
	
	/// <summary>
	/// Resolves model parents recursively and merges properties
	/// </summary>
	private static MinecraftModel ResolveModelParents(MinecraftModel model)
	{
		if (model == null)
			return null;
		
		var loader = MinecraftJsonLoader.Instance;
		var resolved = new MinecraftModel
		{
			Textures = new Dictionary<string, string>(),
			Elements = new List<ModelElement>(),
			Display = model.Display,
			AmbientOcclusion = model.AmbientOcclusion,
			GuiLight = model.GuiLight
		};
		
		// Build inheritance chain
		var chain = new List<MinecraftModel>();
		var current = model;
		var visited = new HashSet<string>();
		
		GD.Print($"Building parent chain starting from model with parent: {model.Parent ?? "none"}");
		
		while (current != null)
		{
			chain.Add(current);
			GD.Print($"  Chain depth {chain.Count}: parent={current.Parent ?? "none"}, elements={current.Elements?.Count ?? 0}, textures={current.Textures?.Count ?? 0}");
			
			if (string.IsNullOrEmpty(current.Parent))
			{
				GD.Print("  Reached root of parent chain");
				break;
			}
			
			// Prevent infinite loops
			if (visited.Contains(current.Parent))
			{
				GD.PrintErr($"Circular parent reference detected: {current.Parent}");
				break;
			}
			visited.Add(current.Parent);
			
			// Load parent model
			var parentPath = NormalizeModelPath(current.Parent);
			GD.Print($"  Loading parent: {current.Parent} (normalized: {parentPath})");
			current = GetModelByPath(parentPath);
			
			if (current == null)
			{
				GD.PrintErr($"  Failed to load parent model: {parentPath}");
				break;
			}
		}
		
		GD.Print($"Parent chain built with {chain.Count} models, merging in reverse order...");
		
		// Merge from root parent down to child (reverse order)
		chain.Reverse();
		
		foreach (var m in chain)
		{
			// Merge textures (child overrides parent)
			if (m.Textures != null)
			{
				foreach (var kvp in m.Textures)
				{
					resolved.Textures[kvp.Key] = kvp.Value;
				}
			}
			
			// Elements from first model to define them
			if (m.Elements != null && m.Elements.Count > 0 && resolved.Elements.Count == 0)
			{
				resolved.Elements = new List<ModelElement>(m.Elements);
				GD.Print($"  Inherited {m.Elements.Count} elements from parent");
			}
			
			// Other properties
			if (m.Display != null)
				resolved.Display = m.Display;
		}
		
		GD.Print($"Final resolved model has {resolved.Elements?.Count ?? 0} elements and {resolved.Textures?.Count ?? 0} textures");
		
		// Resolve texture variables in textures dictionary
		ResolveTextureVariables(resolved);
		
		return resolved;
	}
	
	/// <summary>
	/// Resolves texture variables (#all, #side, etc.) to actual texture paths
	/// </summary>
	private static void ResolveTextureVariables(MinecraftModel model)
	{
		if (model.Textures == null)
			return;
		
		// Keep resolving until no more variables
		bool resolved;
		int maxIterations = 10; // Prevent infinite loops
		int iteration = 0;
		
		do
		{
			resolved = false;
			iteration++;
			
			foreach (var key in model.Textures.Keys.ToList())
			{
				var value = model.Textures[key];
				
				// Check if value is a variable reference (#...)
				if (value.StartsWith("#"))
				{
					var varName = value.Substring(1);
					if (model.Textures.ContainsKey(varName))
					{
						model.Textures[key] = model.Textures[varName];
						resolved = true;
					}
				}
			}
		} while (resolved && iteration < maxIterations);
	}
	
	/// <summary>
	/// Gets a model by a potentially nested path
	/// </summary>
	private static MinecraftModel GetModelByPath(string path)
	{
		var loader = MinecraftJsonLoader.Instance;
		
		GD.Print($"GetModelByPath: searching for path '{path}'");
		
		// Try direct path
		if (loader.HasModel(path))
		{
			GD.Print($"  ✓ Found with direct path: {path}");
			return loader.GetModel(path);
		}
		
		//Try with .json extension
		if (loader.HasModel(path + ".json"))
		{
			GD.Print($"  ✓ Found with .json extension: {path}.json");
			return loader.GetModel(path + ".json");
		}
		
		// Build comprehensive list of patterns to try
		var patterns = new List<string>();
		
		// Standard patterns
		patterns.Add($"models/{path}");
		patterns.Add($"models/{path}.json");
		
		// With minecraft namespace
		patterns.Add($"minecraft/models/{path}");
		patterns.Add($"minecraft/models/{path}.json");
		
		// With assets prefix
		patterns.Add($"assets/minecraft/models/{path}");
		patterns.Add($"assets/minecraft/models/{path}.json");
		
		// Try path variations with different separators
		var pathWithBackslash = path.Replace("/", "\\");
		var pathWithForwardSlash = path.Replace("\\", "/");
		
		if (pathWithBackslash != path)
		{
			patterns.Add($"models\\{pathWithBackslash}");
			patterns.Add($"models\\{pathWithBackslash}.json");
			patterns.Add($"minecraft\\models\\{pathWithBackslash}");
			patterns.Add($"minecraft\\models\\{pathWithBackslash}.json");
			patterns.Add($"assets\\minecraft\\models\\{pathWithBackslash}");
			patterns.Add($"assets\\minecraft\\models\\{pathWithBackslash}.json");
		}
		
		foreach (var pattern in patterns)
		{
			if (loader.HasModel(pattern))
			{
				GD.Print($"  ✓ Found with pattern: {pattern}");
				return loader.GetModel(pattern);
			}
		}
		
		// Try fuzzy searching - match any path that ends with our target
		var allPaths = loader.GetAllModelPaths().ToList();
		
		// First try: exact ending match
		var exactMatches = allPaths.Where(p =>
		{
			var pNorm = p.Replace("\\", "/").ToLowerInvariant();
			var pathNorm = pathWithForwardSlash.ToLowerInvariant();
			return pNorm.EndsWith(pathNorm) || pNorm.EndsWith(pathNorm + ".json");
		}).ToList();
		
		if (exactMatches.Count > 0)
		{
			GD.Print($"  ✓ Found via exact ending match: {exactMatches[0]}");
			return loader.GetModel(exactMatches[0]);
		}
		
		// Second try: path segment match (match the last segment)
		var segments = pathWithForwardSlash.Split('/');
		var lastSegment = segments[segments.Length - 1].ToLowerInvariant();
		
		var segmentMatches = allPaths.Where(p =>
		{
			var pNorm = p.Replace("\\", "/").ToLowerInvariant();
			var pSegments = pNorm.Split('/');
			var pLast = pSegments[pSegments.Length - 1].Replace(".json", "");
			return pLast == lastSegment || pLast == lastSegment + ".json";
		}).ToList();
		
		if (segmentMatches.Count > 0)
		{
			GD.Print($"  ✓ Found via segment match: {segmentMatches[0]}");
			return loader.GetModel(segmentMatches[0]);
		}
		
		GD.PrintErr($"  ✗ No model found for path: {path}");
		GD.Print("  Available model paths (sample):");
		var samplePaths = allPaths.Take(10);
		foreach (var p in samplePaths)
		{
			GD.Print($"    - {p}");
		}
		
		return null;
	}
	
	/// <summary>
	/// Normalizes a model path to handle minecraft: namespace and paths
	/// </summary>
	private static string NormalizeModelPath(string path)
	{
		if (string.IsNullOrEmpty(path))
			return path;
		
		// Remove minecraft: namespace prefix
		if (path.StartsWith("minecraft:"))
			path = path.Substring("minecraft:".Length);
		
		// Convert to file path format
		path = path.Replace(":", "/");
		
		return path;
	}
	
	/// <summary>
	/// Searches for models matching a pattern
	/// </summary>
	public static IEnumerable<string> SearchModels(string searchTerm)
	{
		var loader = MinecraftJsonLoader.Instance;
		
		if (!loader.IsLoaded)
		{
			return Enumerable.Empty<string>();
		}
		
		return loader.GetAllModelPaths()
			.Where(path => path.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
			.OrderBy(path => path);
	}
	
	/// <summary>
	/// Gets statistics about loaded content
	/// </summary>
	public static (int models, int blockstates, int totalFiles) GetLoadedStats()
	{
		var loader = MinecraftJsonLoader.Instance;
		
		return (
			loader.GetAllModelPaths().Count(),
			loader.GetAllBlockStatePaths().Count(),
			loader.TotalFilesLoaded
		);
	}
	
	/// <summary>
	/// Gets all variant names for a blockstate
	/// </summary>
	public static List<string> GetBlockStateVariants(string blockStateName)
	{
		var blockState = GetBlockStateByName(blockStateName);
		if (blockState == null)
			return new List<string>();
		
		// For multipart blockstates, return a single "Default" variant
		if (blockState.Multipart != null && blockState.Multipart.Count > 0)
		{
			return new List<string> { "" }; // Empty string represents default state
		}
		
		// For variants blockstates
		if (blockState.Variants != null)
		{
			return blockState.Variants.Keys.ToList();
		}
		
		return new List<string>();
	}
	
	/// <summary>
	/// Creates a 3D node from a blockstate variant
	/// </summary>
	public static Node3D CreateNodeFromBlockState(string blockStateName, string variant = "")
	{
		GD.Print($"CreateNodeFromBlockState: blockStateName={blockStateName}, variant={variant}");
		
		var blockState = GetBlockStateByName(blockStateName);
		if (blockState == null)
		{
			GD.PrintErr($"BlockState not found: {blockStateName}");
			return null;
		}
		
		// Check if it's a multipart blockstate
		if (blockState.Multipart != null && blockState.Multipart.Count > 0)
		{
			GD.Print("Creating multipart blockstate node");
			return CreateNodeFromMultipartBlockState(blockState);
		}
		
		// Handle standard variant blockstate
		VariantModel variantData = GetVariantData(blockState, variant);
		
		if (variantData == null || string.IsNullOrEmpty(variantData.Model))
		{
			GD.PrintErr($"No model found for variant: {variant}");
			return null;
		}
		
		GD.Print($"Variant model reference: {variantData.Model}");
		
		// Load the model
		var modelPath = NormalizeModelPath(variantData.Model);
		GD.Print($"Normalized model path: {modelPath}");
		
		var model = GetModelByPath(modelPath);
		
		if (model == null)
		{
			GD.PrintErr($"Could not load model: {variantData.Model} (normalized: {modelPath})");
			return null;
		}
		
		GD.Print("Model loaded successfully, creating node...");
		
		// Create the node from the model, passing rotations to maintain pivot point
		var node = CreateNodeFromModel(model, variantData.X, variantData.Y);
		
		if (node != null)
		{
			GD.Print("Node created successfully with rotations applied to geometry");
		}
		
		return node;
	}
	
	/// <summary>
	/// Creates a 3D node from a multipart blockstate (like fences, which assemble from multiple models)
	/// </summary>
	private static Node3D CreateNodeFromMultipartBlockState(BlockState blockState)
	{
		GD.Print($"CreateNodeFromMultipartBlockState: processing {blockState.Multipart.Count} parts");
		
		var rootNode = new Node3D();
		
		// For basic multipart support, we'll apply all parts that don't have conditions
		// or apply the first part of each conditional group
		foreach (var part in blockState.Multipart)
		{
			VariantModel variantData = null;
			
			// Parse the Apply property
			try
			{
				if (part.Apply is System.Text.Json.JsonElement jsonElement)
				{
					if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
					{
						// Array of models - pick the first one
						var variants = JsonSerializer.Deserialize<List<VariantModel>>(jsonElement.GetRawText());
						variantData = variants?.FirstOrDefault();
					}
					else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
					{
						variantData = JsonSerializer.Deserialize<VariantModel>(jsonElement.GetRawText());
					}
				}
				else if (part.Apply is VariantModel vm)
				{
					variantData = vm;
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Error parsing multipart apply data: {ex.Message}");
				continue;
			}
			
			if (variantData == null || string.IsNullOrEmpty(variantData.Model))
			{
				GD.PrintErr("Multipart has no valid model reference");
				continue;
			}
			
			// For basic support: apply all parts without conditions (When == null)
			// For parts with conditions, only apply if it's the first part (index 0) or has no conditions
			bool shouldApply = part.When == null || part.When.Count == 0;
			
			// If the part has no conditions, always apply it (like fence_post)
			if (shouldApply)
			{
				GD.Print($"  Applying model: {variantData.Model}");
				
				var modelPath = NormalizeModelPath(variantData.Model);
				var model = GetModelByPath(modelPath);
				
				if (model != null)
				{
					// Pass rotations to CreateNodeFromModel to maintain pivot point
					var partNode = CreateNodeFromModel(model, variantData.X, variantData.Y);
					if (partNode != null)
					{
						rootNode.AddChild(partNode);
					}
				}
				else
				{
					GD.PrintErr($"  Could not load model: {variantData.Model}");
				}
			}
		}
		
		if (rootNode.GetChildCount() == 0)
		{
			GD.PrintErr("No models were successfully loaded for this multipart blockstate");
			rootNode.QueueFree();
			return null;
		}
		
		GD.Print($"Successfully created multipart node with {rootNode.GetChildCount()} parts");
		return rootNode;
	}
	
	/// <summary>
	/// Gets variant data from a blockstate, handling both single and array variants
	/// </summary>
	private static VariantModel GetVariantData(BlockState blockState, string variant)
	{
		if (blockState.Variants == null || !blockState.Variants.ContainsKey(variant))
			return null;
		
		var variantObj = blockState.Variants[variant];
		
		// Parse the variant data - could be a single object or array
		try
		{
			// If it's a JsonElement, we need to deserialize it properly
			if (variantObj is System.Text.Json.JsonElement jsonElement)
			{
				if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
				{
					// Pick the first variant from the array
					var variants = JsonSerializer.Deserialize<List<VariantModel>>(jsonElement.GetRawText());
					return variants?.FirstOrDefault();
				}
				else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
				{
					return JsonSerializer.Deserialize<VariantModel>(jsonElement.GetRawText());
				}
			}
			// Try to deserialize as VariantModel directly
			else if (variantObj is VariantModel vm)
			{
				return vm;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing variant data: {ex.Message}");
		}
		
		return null;
	}
}
