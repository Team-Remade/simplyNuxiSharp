using GDExtensionBindgen;
using Godot;
using System.Collections.Generic;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Represents a single block placement in the world.
/// </summary>
public class BlockPlacement
{
	/// <summary>The voxel grid position to place the block at.</summary>
	public Vector3I Position { get; set; }
	
	/// <summary>The block type name (e.g. "stone", "grass_block"). Must match a UniqueName in the VoxelBlockyTypeLibrary.</summary>
	public string BlockName { get; set; }
}

/// <summary>
/// A Node3D that wraps a VoxelTerrain and uses a VoxelGeneratorScript (via GDScript delegate)
/// to generate terrain from an array of BlockPlacements.
///
/// Usage:
///   var gen = new BlockArrayGenerator();
///   AddChild(gen);  // must be in scene tree before Initialize
///   gen.SetBlocks(new[] {
///       new BlockPlacement { Position = new Vector3I(0, 0, 0), BlockName = "stone" },
///       new BlockPlacement { Position = new Vector3I(1, 0, 0), BlockName = "grass_block" },
///   });
///   gen.Initialize(
///       bounds: new Aabb(new Vector3(-64, -16, -64), new Vector3(128, 32, 128)),
///       viewDistance: 64
///   );
/// </summary>
public partial class BlockArrayGenerator : Node3D
{
	private readonly Dictionary<Vector3I, int> _blockMap = new();
	
	/// <summary>The underlying VoxelTerrain node.</summary>
	public VoxelTerrain Terrain { get; private set; }

	public override void _Ready()
	{
		Terrain = new VoxelTerrain
		{
			Mesher = VoxelSettings.Instance.Mesher
		};
		AddChild(Terrain);
	}
	
	/// <summary>
	/// Sets the array of blocks this generator will place.
	/// Resolves block names to IDs using the VoxelBlockyTypeLibrary.
	/// Call this before Initialize().
	/// </summary>
	public void SetBlocks(BlockPlacement[] blocks)
	{
		_blockMap.Clear();
		
		if (blocks == null || blocks.Length == 0)
			return;
		
		var library = VoxelSettings.Instance?.Library;
		if (library == null)
		{
			GD.PrintErr("BlockArrayGenerator: VoxelBlockyTypeLibrary not available");
			return;
		}
		
		int mapped = 0;
		int failed = 0;
		foreach (var block in blocks)
		{
			int blockId = library.GetModelIndexDefault(block.BlockName);
			if (blockId > 0)
			{
				_blockMap[block.Position] = blockId;
				mapped++;
			}
			else
			{
				GD.PrintErr($"BlockArrayGenerator: Block '{block.BlockName}' not found in library");
				failed++;
			}
		}
		
		GD.Print($"BlockArrayGenerator: Mapped {mapped} blocks ({failed} failed)");
	}
	
	/// <summary>
	/// Initializes the terrain with the configured bounds and viewer.
	/// Uses a VoxelGeneratorScript (via GDScript) to generate blocks from the placement array.
	/// </summary>
	/// <param name="bounds">The AABB bounds for the terrain.</param>
	/// <param name="viewDistance">The view distance for the VoxelViewer.</param>
	public void Initialize(Aabb bounds, int viewDistance = 64)
	{
		// Load the GDScript that extends VoxelGeneratorScript
		var script = GD.Load<Script>("res://src/core/block_array_generator_script.gd");
		if (script == null)
		{
			GD.PrintErr("BlockArrayGenerator: Could not load block_array_generator_script.gd");
			return;
		}
		
		// Create a VoxelGeneratorScript instance (the native base type) and attach our GDScript.
		// We must use ClassDB.Instantiate to create the correct native type before attaching the script.
		var generatorObject = ClassDB.Instantiate("VoxelGeneratorScript").AsGodotObject() as Resource;
		if (generatorObject == null)
		{
			GD.PrintErr("BlockArrayGenerator: Could not instantiate VoxelGeneratorScript");
			return;
		}
		generatorObject.SetScript(script);
		
		// Set the generate_block_callable to our C# method.
		// Use Variant-based callable since VoxelBuffer is a GDExtension type.
		generatorObject.Set("generate_block_callable", Callable.From<Variant, Vector3I, int>(OnGenerateBlockVariant));
		
		// Wrap in VoxelGeneratorScript
		var generator = (VoxelGeneratorScript)(Variant)generatorObject;
		
		// Set terrain bounds
		Terrain.Bounds = bounds;
		
		// Assign the generator
		Terrain.Generator = generator;
		
		// Add a VoxelViewer to trigger chunk loading
		var viewer = new VoxelViewer
		{
			ViewDistance = viewDistance,
			RequiresVisuals = true,
			RequiresCollisions = false
		};
		((Node3D)viewer).Name = "Viewer";
		AddChild(viewer);
		
		GD.Print($"BlockArrayGenerator: Initialized with {_blockMap.Count} blocks");
	}
	
	/// <summary>
	/// Variant-based entry point called by the GDScript callable bridge.
	/// Unwraps the VoxelBuffer from Variant and delegates to OnGenerateBlock.
	/// The origin is passed as Vector3I (integer voxel coordinates).
	/// </summary>
	private void OnGenerateBlockVariant(Variant bufferVariant, Vector3I originInVoxels, int lod)
	{
		var buffer = (VoxelBuffer)bufferVariant;
		if (buffer == null)
			return;
		OnGenerateBlock(buffer, originInVoxels, lod);
	}
	
	/// <summary>
	/// Called by the VoxelGeneratorScript for each block chunk that needs to be generated.
	/// Fills the buffer with block IDs from the placement map.
	/// </summary>
	private void OnGenerateBlock(VoxelBuffer buffer, Vector3I originInVoxels, int lod)
	{
		if (_blockMap.Count == 0)
			return;
		
		// Only process LOD 0 (full resolution)
		if (lod != 0)
			return;
		
		var bufferSize = buffer.GetSize();
		
		// Check each block in our map to see if it falls within this buffer's region
		foreach (var kvp in _blockMap)
		{
			var worldPos = kvp.Key;
			var localPos = worldPos - originInVoxels;
			
			// Check if this position is within the buffer
			if (localPos.X >= 0 && localPos.X < bufferSize.X &&
			    localPos.Y >= 0 && localPos.Y < bufferSize.Y &&
			    localPos.Z >= 0 && localPos.Z < bufferSize.Z)
			{
				buffer.SetVoxel(kvp.Value, localPos.X, localPos.Y, localPos.Z, (int)VoxelBuffer.ChannelId.ChannelType);
			}
		}
	}
}
