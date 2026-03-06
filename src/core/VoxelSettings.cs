using GDExtensionBindgen;
using Godot;

namespace simplyRemadeNuxi.core;

public partial class VoxelSettings : Node
{
	// Use this to get an instance of the mesher when creating new terrain objects.
	public static VoxelSettings Instance { get; private set; }
	
	// This will generate the terrain mesh for us
	public VoxelMesherBlocky Mesher;
	
	// Stores all the voxels found from the assets
	private VoxelBlockyTypeLibrary _library;

	public override void _Ready()
	{
		Instance = this;
	}
	
	public void CreateLibrary(VoxelBlockyType[] voxels)
	{
		_library = new VoxelBlockyTypeLibrary();
		
		foreach (var voxel in voxels)
		{
			_library.Types.Add(voxel);
		}
		
		Mesher = new VoxelMesherBlocky
		{
			Library = _library
		};
	}
}