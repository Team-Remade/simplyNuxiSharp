@tool
extends MeshInstance3D
## Minecraft-style 3D cloud layer.
##
## Uses a large subdivided PlaneMesh.  The MinecraftClouds spatial shader
## samples the cloud texture in world space (snapped to texel centres) and
## extrudes white vertices upward, discarding black pixels in the fragment
## stage.  The mesh follows the camera smoothly on X/Z — no snapping needed
## because the shader's world-space UV is always aligned to the texture grid.
##
## Usage:
##   1. Add a MeshInstance3D to your scene.
##   2. Attach this script.
##   3. Assign cloud_material (MinecraftClouds.tres) in the Inspector.
##   4. Position the node at the desired cloud altitude (e.g. y = 128).

## The ShaderMaterial to apply (MinecraftClouds.tres).
@export var cloud_material: ShaderMaterial

## Subdivisions per axis.  Must be at least tex_width (or tex_height) so each
## texel maps to at least one vertex for correct extrusion.
@export_range(32, 1024) var subdivisions: int = 256

func _ready() -> void:
	_build_mesh()

func _process(_delta: float) -> void:
	_follow_camera()

# ── Mesh generation ───────────────────────────────────────────────────────────
func _build_mesh() -> void:
	if cloud_material == null:
		push_warning("MinecraftClouds: no cloud_material assigned.")
		return

	# Read texture size to set tile_world_size and texture_size uniforms.
	var tex: Texture2D = cloud_material.get_shader_parameter("cloud_texture")
	var tex_w: float = 128.0
	var tex_h: float = 128.0
	if tex != null:
		tex_w = float(tex.get_width())
		tex_h = float(tex.get_height())

	# 3 metres per pixel.
	var cell: float = 3.0
	var mesh_w: float = tex_w * cell
	var mesh_h: float = tex_h * cell

	var plane := PlaneMesh.new()
	plane.size            = Vector2(mesh_w, mesh_h)
	plane.subdivide_width  = subdivisions
	plane.subdivide_depth  = subdivisions
	plane.orientation     = PlaneMesh.FACE_Y

	mesh              = plane
	material_override = cloud_material

	# Keep shader uniforms in sync.
	cloud_material.set_shader_parameter("tile_world_size", Vector2(mesh_w, mesh_h))
	cloud_material.set_shader_parameter("texture_size",    Vector2(tex_w, tex_h))

# ── Camera follow (smooth — no snapping) ─────────────────────────────────────
func _follow_camera() -> void:
	var cam: Camera3D = get_viewport().get_camera_3d()
	if cam == null:
		return
	# Follow the camera exactly on X/Z; keep the node's own Y (cloud altitude).
	var cam_pos: Vector3 = cam.global_position
	global_position = Vector3(cam_pos.x, global_position.y, cam_pos.z)
