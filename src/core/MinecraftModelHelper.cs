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
	public static MeshInstance3D CreateMeshFromElement(ModelElement element, Dictionary<string, string> textures)
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
		
		var size = to - from;
		var center = (from + to) / 2.0f;
		
		// Create a custom mesh with proper UV mapping
		var arrayMesh = CreateCubeMeshWithTextures(from, to, element.Faces, textures);
		
		meshInstance.Mesh = arrayMesh;
		meshInstance.Position = center;
		
		// Apply rotation if present
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
	/// </summary>
	private static ArrayMesh CreateCubeMeshWithTextures(Vector3 from, Vector3 to, Dictionary<string, ElementFace> faces, Dictionary<string, string> textures)
	{
		float scale = 1.0f / 16.0f;
		var size = to - from;
		
		// Create arrays for mesh data
		var vertices = new List<Vector3>();
		var uvs = new List<Vector2>();
		var normals = new List<Vector3>();
		var indices = new List<int>();
		
		// Helper to add a quad (counter-clockwise winding for outward facing)
		void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
		{
			int baseIndex = vertices.Count;
			
			vertices.Add(v0);
			vertices.Add(v1);
			vertices.Add(v2);
			vertices.Add(v3);
			
			uvs.Add(uv0);
			uvs.Add(uv1);
			uvs.Add(uv2);
			uvs.Add(uv3);
			
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);
			
			// Two triangles for the quad - reversed winding order
			indices.Add(baseIndex);
			indices.Add(baseIndex + 2);
			indices.Add(baseIndex + 1);
			
			indices.Add(baseIndex);
			indices.Add(baseIndex + 3);
			indices.Add(baseIndex + 2);
		}
		
		// Calculate UV coordinates from face data (normalized 0-1 range)
		// Minecraft UV format: [x1, y1, x2, y2] where (x1,y1) is top-left and (x2,y2) is bottom-right in texture space
		Vector2[] GetFaceUVs(ElementFace face)
		{
			if (face?.UV != null && face.UV.Length == 4)
			{
				// Minecraft UV is in 0-16 range, convert to 0-1
				// Also note: Minecraft Y axis is inverted (0 is top, 16 is bottom)
				// But Godot UV space has 0 at top, 1 at bottom, so we need to invert Y
				float u1 = face.UV[0] / 16.0f;
				float v1 = face.UV[1] / 16.0f;
				float u2 = face.UV[2] / 16.0f;
				float v2 = face.UV[3] / 16.0f;
				
				// Return in order: bottom-left, top-left, top-right, bottom-right
				// This matches the vertex order we use in AddQuad calls
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
		
		// Center the mesh at origin (will be positioned by parent)
		var halfSize = size / 2.0f;
		
		// Down face (y-) - Bottom face, normal pointing down (0, -1, 0)
		// When looking up at the bottom, we see it from below
		if (faces != null && faces.ContainsKey("down"))
		{
			var faceUVs = GetFaceUVs(faces["down"]);
			AddQuad(
				new Vector3(halfSize.X, -halfSize.Y, -halfSize.Z),   // v0: front-right
				new Vector3(halfSize.X, -halfSize.Y, halfSize.Z),    // v1: back-right
				new Vector3(-halfSize.X, -halfSize.Y, halfSize.Z),   // v2: back-left
				new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),  // v3: front-left
				new Vector3(0, -1, 0),  // Normal pointing down
				faceUVs[0], faceUVs[1], faceUVs[2], faceUVs[3]
			);
		}
		
		// Up face (y+) - Top face, normal pointing up (0, 1, 0)
		// When looking down at the top, we see it from above
		if (faces != null && faces.ContainsKey("up"))
		{
			var faceUVs = GetFaceUVs(faces["up"]);
			AddQuad(
				new Vector3(-halfSize.X, halfSize.Y, -halfSize.Z),   // v0: front-left
				new Vector3(-halfSize.X, halfSize.Y, halfSize.Z),    // v1: back-left
				new Vector3(halfSize.X, halfSize.Y, halfSize.Z),     // v2: back-right
				new Vector3(halfSize.X, halfSize.Y, -halfSize.Z),    // v3: front-right
				new Vector3(0, 1, 0),  // Normal pointing up
				faceUVs[0], faceUVs[1], faceUVs[2], faceUVs[3]
			);
		}
		
		// North face (z-) - Front face in Minecraft coords, normal pointing forward (0, 0, -1)
		if (faces != null && faces.ContainsKey("north"))
		{
			var faceUVs = GetFaceUVs(faces["north"]);
			AddQuad(
				new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),  // v0: bottom-left
				new Vector3(-halfSize.X, halfSize.Y, -halfSize.Z),   // v1: top-left
				new Vector3(halfSize.X, halfSize.Y, -halfSize.Z),    // v2: top-right
				new Vector3(halfSize.X, -halfSize.Y, -halfSize.Z),   // v3: bottom-right
				new Vector3(0, 0, -1),  // Normal pointing north (negative Z)
				faceUVs[0], faceUVs[1], faceUVs[2], faceUVs[3]
			);
		}
		
		// South face (z+) - Back face in Minecraft coords, normal pointing back (0, 0, 1)
		if (faces != null && faces.ContainsKey("south"))
		{
			var faceUVs = GetFaceUVs(faces["south"]);
			AddQuad(
				new Vector3(halfSize.X, -halfSize.Y, halfSize.Z),    // v0: bottom-right
				new Vector3(halfSize.X, halfSize.Y, halfSize.Z),     // v1: top-right
				new Vector3(-halfSize.X, halfSize.Y, halfSize.Z),    // v2: top-left
				new Vector3(-halfSize.X, -halfSize.Y, halfSize.Z),   // v3: bottom-left
				new Vector3(0, 0, 1),  // Normal pointing south (positive Z)
				faceUVs[0], faceUVs[1], faceUVs[2], faceUVs[3]
			);
		}
		
		// West face (x-) - Left face, normal pointing left (-1, 0, 0)
		if (faces != null && faces.ContainsKey("west"))
		{
			var faceUVs = GetFaceUVs(faces["west"]);
			AddQuad(
				new Vector3(-halfSize.X, -halfSize.Y, halfSize.Z),   // v0: bottom-back
				new Vector3(-halfSize.X, halfSize.Y, halfSize.Z),    // v1: top-back
				new Vector3(-halfSize.X, halfSize.Y, -halfSize.Z),   // v2: top-front
				new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),  // v3: bottom-front
				new Vector3(-1, 0, 0),  // Normal pointing west (negative X)
				faceUVs[0], faceUVs[1], faceUVs[2], faceUVs[3]
			);
		}
		
		// East face (x+) - Right face, normal pointing right (1, 0, 0)
		if (faces != null && faces.ContainsKey("east"))
		{
			var faceUVs = GetFaceUVs(faces["east"]);
			AddQuad(
				new Vector3(halfSize.X, -halfSize.Y, -halfSize.Z),   // v0: bottom-front
				new Vector3(halfSize.X, halfSize.Y, -halfSize.Z),    // v1: top-front
				new Vector3(halfSize.X, halfSize.Y, halfSize.Z),     // v2: top-back
				new Vector3(halfSize.X, -halfSize.Y, halfSize.Z),    // v3: bottom-back
				new Vector3(1, 0, 0),  // Normal pointing east (positive X)
				faceUVs[0], faceUVs[1], faceUVs[2], faceUVs[3]
			);
		}
		
		// Create ArrayMesh
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		
		return arrayMesh;
	}
	
	/// <summary>
	/// Creates a complete 3D node from a Minecraft model with parent resolution
	/// </summary>
	public static Node3D CreateNodeFromModel(MinecraftModel model)
	{
		if (model == null)
		{
			return null;
		}
		
		GD.Print($"CreateNodeFromModel: Starting with model (parent: {model.Parent ?? "none"})");
		
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
		
		var root = new Node3D();
		
		// Create meshes for each element
		foreach (var element in resolvedModel.Elements)
		{
			var mesh = CreateMeshFromElement(element, resolvedModel.Textures);
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
		
		// Collect all unique textures used by this element
		var usedTextures = new HashSet<string>();
		foreach (var face in faces.Values)
		{
			if (face?.Texture != null)
			{
				var texturePath = ResolveTexturePath(face.Texture, textures);
				if (texturePath != null)
				{
					usedTextures.Add(texturePath);
				}
			}
		}
		
		// For now, use the first texture found (later we can implement multi-material support)
		if (usedTextures.Count > 0)
		{
			var texturePath = usedTextures.First();
			var texture = LoadMinecraftTexture(texturePath);
			
			StandardMaterial3D material;
			if (texture != null)
			{
				material = new StandardMaterial3D();
				material.AlbedoColor = Colors.White;  // Use white to show texture as-is
				material.AlbedoTexture = texture;
				material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest; // Pixel-perfect for Minecraft style
				
				// Enable alpha clipping for transparency
				material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
				material.AlphaScissorThreshold = 0.5f;
			}
			else
			{
				// Fallback to default material
				material = new StandardMaterial3D();
				material.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
			}
			
			// Apply material to mesh surface instead of using MaterialOverride
			if (meshInstance.Mesh is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
			{
				arrayMesh.SurfaceSetMaterial(0, material);
			}
		}
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
		
		// Remove minecraft: namespace if present
		if (texturePath.StartsWith("minecraft:"))
			texturePath = texturePath.Substring("minecraft:".Length);
		
		// Build possible paths to check
		var userDataPath = OS.GetUserDataDir();
		var assetsPath = System.IO.Path.Combine(userDataPath, "data", "SimplyRemadeAssetsV1");
		
		var possiblePaths = new List<string>
		{
			System.IO.Path.Combine(assetsPath, texturePath + ".png"),
			System.IO.Path.Combine(assetsPath, "textures", texturePath + ".png"),
			System.IO.Path.Combine(assetsPath, "minecraft", "textures", texturePath + ".png"),
			System.IO.Path.Combine(assetsPath, "assets", "minecraft", "textures", texturePath + ".png"),
			System.IO.Path.Combine(assetsPath, texturePath.Replace("/", "\\") + ".png"),
			System.IO.Path.Combine(assetsPath, "textures", texturePath.Replace("/", "\\") + ".png"),
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
						GD.Print($"  ✓ Loaded texture: {path}");
						return texture;
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Error loading texture {path}: {ex.Message}");
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
		if (blockState == null || blockState.Variants == null)
			return new List<string>();
		
		return blockState.Variants.Keys.ToList();
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
		
		// Get the variant data
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
		
		// Create the node from the model
		var node = CreateNodeFromModel(model);
		
		if (node != null)
		{
			// Apply rotations from the variant
			if (variantData.X != 0)
			{
				node.RotateX(Mathf.DegToRad(variantData.X));
			}
			if (variantData.Y != 0)
			{
				node.RotateY(Mathf.DegToRad(variantData.Y));
			}
			
			GD.Print("Node created successfully");
		}
		
		return node;
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
