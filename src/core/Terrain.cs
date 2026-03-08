using GDExtensionBindgen;
using Godot;

namespace simplyRemadeNuxi.core;

public partial class Terrain : Node3D
{
	public VoxelTerrain terrain = new();

	// Do shit to the terrain variable
	public override void _Ready()
	{
		terrain.Mesher = VoxelSettings.Instance.Mesher;
		AddChild(terrain);
	}

	public VoxelBlockyTypeLibrary GetLibrary()
	{
		return VoxelSettings.Instance?.Library;
	}
}