extends VoxelGeneratorScript

## A VoxelGeneratorScript that delegates block generation to a C# callable.
## Set the generate_block_callable before using this generator.

var generate_block_callable: Callable

func _generate_block(out_buffer: VoxelBuffer, origin_in_voxels: Vector3i, lod: int) -> void:
	if generate_block_callable.is_valid():
		generate_block_callable.call(out_buffer, origin_in_voxels, lod)
