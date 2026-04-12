using GDExtensionBindgen;
using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace simplyRemadeNuxi.core;

public partial class VoxelSettings : Node
{
	// Use this to get an instance of the mesher when creating new terrain objects.
	public static VoxelSettings Instance { get; private set; }
	
	// This will generate the terrain mesh for us
	public VoxelMesherBlocky Mesher;
	
	// Stores all the voxels found from the assets
	private VoxelBlockyTypeLibrary _library;
	
	// Public accessor for the library
	public VoxelBlockyTypeLibrary Library => _library;

	public override void _Ready()
	{
		Instance = this;
	}
	
	/// <summary>
	/// Builds the VoxelBlockyTypeLibrary from the block models loaded by MinecraftJsonLoader.
	/// Call this after the asset load has completed (i.e., after LoadMinecraftJsonFiles finishes).
	/// </summary>
	/// <param name="progressCallback">Optional callback to report progress (message, percentage 0-100).</param>
	public async Task<bool> BuildLibraryFromLoadedModels(System.Action<string, int> progressCallback = null)
	{
		var loader = MinecraftJsonLoader.Instance;
		
		if (!loader.IsLoaded)
		{
			GD.PrintErr("VoxelSettings: Cannot build library - Minecraft JSON files are not loaded yet!");
			return false;
		}
		
		GD.Print("VoxelSettings: Building voxel library from loaded block models...");
		progressCallback?.Invoke("Scanning block models...", 0);
		await Task.Delay(1); // Allow other operations to run
		
		// Get all block model paths (filter to only block models, not item models)
		var blockModelPaths = loader.GetAllModelPaths()
			.Where(p => p.Replace("\\", "/").Contains("models/block"))
			.ToList();
		
		GD.Print($"VoxelSettings: Found {blockModelPaths.Count} block model paths");
		progressCallback?.Invoke($"Found {blockModelPaths.Count} block models", 5);
		await Task.Delay(1); // Allow other operations to run
		
		var voxelTypes = new List<VoxelBlockyType>();
		
		// Add a default empty/air type at index 0 (required by VoxelBlockyTypeLibrary)
		var airType = new VoxelBlockyType();
		airType.UniqueName = "air";
		var emptyModel = new VoxelBlockyModelEmpty();
		airType.BaseModel = emptyModel;
		voxelTypes.Add(airType);
		
		int successCount = 0;
		int failCount = 0;
		int processed = 0;
		int total = blockModelPaths.Count;
		
		foreach (var modelPath in blockModelPaths)
		{
			processed++;
			
			// Report progress every 50 models
			if (processed % 50 == 0 || processed == total)
			{
				int pct = 5 + (int)((processed / (float)total) * 90);
				progressCallback?.Invoke($"Building voxel models: {processed}/{total}", pct);
				await Task.Delay(1); // Allow other operations to run
			}
			
			try
			{
				var model = loader.GetModel(modelPath);
				if (model == null)
					continue;
				
				// Skip template/parent models that have no concrete textures
				// (i.e., models whose own textures are all unresolved #variable references)
				if (IsTemplateModel(model))
					continue;
				
				// Skip models that have no elements and no parent (they can't produce geometry)
				if ((model.Elements == null || model.Elements.Count == 0) && string.IsNullOrEmpty(model.Parent))
					continue;
				
				// Derive a unique name from the path (e.g., "models/block/stone.json" -> "stone")
				var blockName = Path.GetFileNameWithoutExtension(modelPath);
				if (string.IsNullOrEmpty(blockName))
					continue;
				
				// Create the 3D node from the model to get the mesh
				var modelNode = MinecraftModelHelper.CreateNodeFromModel(model);
				if (modelNode == null)
				{
					// Model failed to resolve (e.g., missing parent chain) - skip silently
					continue;
				}
				
				// Combine all MeshInstance3D children into a single ArrayMesh with atlas
				var combinedMesh = CombineMeshesFromNode(modelNode, blockName);
				modelNode.QueueFree();
				
				if (combinedMesh == null || combinedMesh.GetSurfaceCount() == 0)
				{
					failCount++;
					continue;
				}
				
				// Create the VoxelBlockyModelMesh
				var voxelModel = new VoxelBlockyModelMesh();
				voxelModel.Mesh = combinedMesh;
				
				// Create the VoxelBlockyType
				var voxelType = new VoxelBlockyType();
				voxelType.UniqueName = blockName;
				voxelType.BaseModel = voxelModel;
				
				voxelTypes.Add(voxelType);
				successCount++;
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"VoxelSettings: Failed to create voxel type for {modelPath}: {ex.Message}");
				failCount++;
			}
		}
		
		GD.Print($"VoxelSettings: Created {successCount} voxel types ({failCount} failed)");
		progressCallback?.Invoke($"Baking library ({successCount} block types)...", 95);
		
		CreateLibrary(voxelTypes.ToArray());
		
		progressCallback?.Invoke($"Voxel library ready: {voxelTypes.Count} types", 100);

		return true;
	}
	
	/// <summary>
	/// Returns true if the model is a template/parent model that has no concrete texture assignments.
	/// A model is a template if all its own texture values are unresolved variable references (#varname),
	/// meaning it requires a child model to provide the actual texture paths.
	/// </summary>
	private static bool IsTemplateModel(MinecraftModel model)
	{
		// If the model has no textures at all, it's a template (no concrete data)
		if (model.Textures == null || model.Textures.Count == 0)
			return true;
		
		// If ALL texture values are unresolved variable references (#varname), it's a template
		// A concrete model must have at least one non-variable texture value
		bool hasConcreteTexture = model.Textures.Values.Any(v =>
			v != null && !v.StartsWith("#"));
		
		return !hasConcreteTexture;
	}
	
	/// <summary>
	/// Combines all MeshInstance3D children of a node into a single-surface ArrayMesh
	/// using a texture atlas. All unique textures are packed into one atlas image,
	/// and UV coordinates are remapped to the corresponding atlas region.
	/// This produces a single surface with one material, satisfying VoxelBlockyModelMesh's constraints.
	/// </summary>
	// Plant/foliage keywords that require a green biome tint (same list as ProjectPropertiesPanel)
	private static readonly string[] FoliageKeywords = {
		"grass", "leaves", "vine", "lily_pad", "fern", "tall_grass",
		"seagrass", "kelp", "sugar_cane", "bamboo",
		"attached_melon_stem", "attached_pumpkin_stem", "melon_stem", "pumpkin_stem"
	};
	
	/// <summary>
	/// Returns true if the block name matches any foliage/plant keyword that needs a green tint.
	/// </summary>
	private static bool IsFoliageBlock(string blockName)
	{
		if (string.IsNullOrEmpty(blockName)) return false;
		foreach (var keyword in FoliageKeywords)
		{
			if (blockName.Contains(keyword, System.StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}
	
	private static ArrayMesh CombineMeshesFromNode(Node3D node, string blockName = null)
	{
		// Step 1: Collect all surfaces with their materials and geometry
		var surfaces = new List<(
			Material mat,
			Vector3[] verts,
			Vector3[] normals,
			Vector2[] uvs,
			int[] indices,
			Transform3D transform)>();
		
		foreach (var child in node.GetChildren())
		{
			if (child is MeshInstance3D meshInstance && meshInstance.Mesh is ArrayMesh sourceMesh)
			{
				var transform = meshInstance.Transform;
				for (int s = 0; s < sourceMesh.GetSurfaceCount(); s++)
				{
					var arrays = sourceMesh.SurfaceGetArrays(s);
					var mat = sourceMesh.SurfaceGetMaterial(s);
					
					Vector3[] verts = null;
					Vector3[] normals = null;
					Vector2[] uvs = null;
					int[] indices = null;
					
					var vv = arrays[(int)Mesh.ArrayType.Vertex];
					if (vv.VariantType == Variant.Type.PackedVector3Array) verts = vv.AsVector3Array();
					
					var nv = arrays[(int)Mesh.ArrayType.Normal];
					if (nv.VariantType == Variant.Type.PackedVector3Array) normals = nv.AsVector3Array();
					
					var uv = arrays[(int)Mesh.ArrayType.TexUV];
					if (uv.VariantType == Variant.Type.PackedVector2Array) uvs = uv.AsVector2Array();
					
					var iv = arrays[(int)Mesh.ArrayType.Index];
					if (iv.VariantType == Variant.Type.PackedInt32Array) indices = iv.AsInt32Array();
					
					if (verts != null && verts.Length > 0)
						surfaces.Add((mat, verts, normals, uvs, indices, transform));
				}
			}
		}
		
		if (surfaces.Count == 0)
			return new ArrayMesh();
		
		// Step 2: Build a texture atlas from all unique textures
		// Map each material to its atlas slot index
		var materialToSlot = new Dictionary<Material, int>();
		var slotTextures = new List<ImageTexture>();
		
		foreach (var (mat, _, _, _, _, _) in surfaces)
		{
			if (mat != null && !materialToSlot.ContainsKey(mat))
			{
				int slot = slotTextures.Count;
				materialToSlot[mat] = slot;
				
				// Extract the albedo texture from the material
				ImageTexture tex = null;
				if (mat is StandardMaterial3D stdMat)
					tex = stdMat.AlbedoTexture as ImageTexture;
				
				slotTextures.Add(tex); // null = no texture for this slot
			}
		}
		
		int numSlots = slotTextures.Count;
		if (numSlots == 0) numSlots = 1; // Ensure at least 1 slot
		
		// Step 3: Pack textures into a horizontal atlas
		// All Minecraft textures are 16x16 (or multiples), use 16x16 as the tile size
		const int TileSize = 16;
		int atlasWidth = TileSize * numSlots;
		int atlasHeight = TileSize;
		
		var atlasImage = Image.Create(atlasWidth, atlasHeight, false, Image.Format.Rgba8);
		atlasImage.Fill(Colors.Transparent);
		
		for (int i = 0; i < slotTextures.Count; i++)
		{
			var tex = slotTextures[i];
			if (tex == null)
				continue;
			
			var srcImage = tex.GetImage();
			if (srcImage == null)
				continue;
			
			// Convert to Rgba8 format to match the atlas image format
			if (srcImage.GetFormat() != Image.Format.Rgba8)
				srcImage.Convert(Image.Format.Rgba8);
			
			// Scale to TileSize x TileSize if needed
			if (srcImage.GetWidth() != TileSize || srcImage.GetHeight() != TileSize)
				srcImage.Resize(TileSize, TileSize, Image.Interpolation.Nearest);
			
			// Apply grass/foliage tint if the block name matches foliage keywords
			// Minecraft grass/foliage textures are grayscale and need a green biome tint
			if (IsFoliageBlock(blockName))
				ApplyTint(srcImage, new Color(145f / 255f, 189f / 255f, 89f / 255f)); // #91BD59 temperate biome grass
			
			// Blit into atlas at slot position
			atlasImage.BlitRect(srcImage, new Rect2I(0, 0, TileSize, TileSize), new Vector2I(i * TileSize, 0));
		}
		
		var atlasTexture = ImageTexture.CreateFromImage(atlasImage);
		
		// Create atlas material
		var atlasMaterial = new StandardMaterial3D();
		atlasMaterial.AlbedoTexture = atlasTexture;
		atlasMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
		atlasMaterial.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
		atlasMaterial.AlphaScissorThreshold = 0.5f;
		
		// Step 4: Combine all surfaces into one, remapping UVs to atlas regions
		var allVerts = new List<Vector3>();
		var allNormals = new List<Vector3>();
		var allUVs = new List<Vector2>();
		var allIndices = new List<int>();
		
		foreach (var (mat, verts, normals, uvs, indices, transform) in surfaces)
		{
			var normalBasis = transform.Basis.Inverse().Transposed();
			int indexOffset = allVerts.Count;
			
			// Determine atlas UV offset for this material
			float uOffset = 0f;
			float uScale = 1f;
			if (mat != null && materialToSlot.TryGetValue(mat, out int slot) && numSlots > 0)
			{
				uOffset = (float)slot / numSlots;
				uScale = 1f / numSlots;
			}
			
			// Transform vertices into parent space
			foreach (var v in verts)
				allVerts.Add(transform * v);
			
			// Transform normals
			if (normals != null)
			{
				foreach (var n in normals)
					allNormals.Add((normalBasis * n).Normalized());
			}
			
			// Remap UVs to atlas region: u' = uOffset + u * uScale
			if (uvs != null)
			{
				foreach (var uv in uvs)
					allUVs.Add(new Vector2(uOffset + uv.X * uScale, uv.Y));
			}
			
			// Offset indices
			if (indices != null)
			{
				foreach (var idx in indices)
					allIndices.Add(idx + indexOffset);
			}
		}
		
		if (allVerts.Count == 0)
			return new ArrayMesh();
		
		// Step 5: Build the final single-surface ArrayMesh
		var mergedArrays = new Godot.Collections.Array();
		mergedArrays.Resize((int)Mesh.ArrayType.Max);
		mergedArrays[(int)Mesh.ArrayType.Vertex] = allVerts.ToArray();
		
		if (allNormals.Count == allVerts.Count)
			mergedArrays[(int)Mesh.ArrayType.Normal] = allNormals.ToArray();
		
		if (allUVs.Count == allVerts.Count)
			mergedArrays[(int)Mesh.ArrayType.TexUV] = allUVs.ToArray();
		
		if (allIndices.Count > 0)
			mergedArrays[(int)Mesh.ArrayType.Index] = allIndices.ToArray();
		
		var combinedMesh = new ArrayMesh();
		combinedMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, mergedArrays);
		combinedMesh.SurfaceSetMaterial(0, atlasMaterial);
		
		return combinedMesh;
	}
	
	/// <summary>
	/// Multiplies each pixel's RGB by the given tint color (preserves alpha).
	/// </summary>
	private static void ApplyTint(Image image, Color tint)
	{
		int width = image.GetWidth();
		int height = image.GetHeight();
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				var pixel = image.GetPixel(x, y);
				image.SetPixel(x, y, new Color(
					pixel.R * tint.R,
					pixel.G * tint.G,
					pixel.B * tint.B,
					pixel.A));
			}
		}
	}
	
	public void CreateLibrary(VoxelBlockyType[] voxels)
	{
		_library = new VoxelBlockyTypeLibrary();
		
		// Build the types array and set it all at once.
		// Note: Godot.Collections.Array properties return copies, so we must
		// build the array locally and assign it back via the setter.
		var typesArray = new Godot.Collections.Array();
		foreach (var voxel in voxels)
		{
			typesArray.Add((Variant)voxel);
		}
		_library.Types = typesArray;
		
		// Bake the library so lookups (GetModelIndexDefault etc.) work correctly
		_library.Bake();
		
		Mesher = new VoxelMesherBlocky
		{
			Library = _library
		};
		
		GD.Print($"VoxelSettings: Library created with {voxels.Length} voxel types");
	}
}
