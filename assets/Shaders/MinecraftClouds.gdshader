shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_lambert, specular_disabled, unshaded;

// ── Minecraft-style 3D cloud slab shader ─────────────────────────────────────
// Apply to a large subdivided PlaneMesh that follows the camera smoothly on
// X/Z (no snapping needed).  The vertex shader samples the cloud texture in
// world space and extrudes white pixels upward.  The fragment shader discards
// black pixels.  Because UVs are snapped to texel centres before sampling,
// there are no floating-point boundary artefacts.

// Cloud texture (black = no cloud, white = cloud).
uniform sampler2D cloud_texture : source_color, hint_default_white, filter_nearest, repeat_enable;

// World-space size of one full texture tile in metres.
// Set to (tex_width * metres_per_pixel, tex_height * metres_per_pixel).
uniform vec2 tile_world_size = vec2(384.0, 384.0);

// Texture resolution (set to match cloud_texture dimensions).
uniform vec2 texture_size = vec2(128.0, 128.0);

// Maximum extrusion height in metres for fully-white pixels.
uniform float extrude_height : hint_range(0.0, 10.0) = 3.0;

// Threshold: pixels below this luminance are discarded.
uniform float alpha_threshold : hint_range(0.0, 1.0) = 0.3;

// Cloud colour multiplier.
uniform vec4 cloud_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// ── Varying ───────────────────────────────────────────────────────────────────
varying float v_density;   // cloud density at this vertex (0 or 1)
varying vec2  v_world_uv;  // world-space UV for fragment discard

// ── Helpers ───────────────────────────────────────────────────────────────────
// Snap a world-space UV to the nearest texel centre to avoid boundary bleed.
vec2 snap_uv(vec2 world_pos) {
	// Convert world XZ to texel coordinates, floor to nearest texel, then back.
	vec2 texel = floor(world_pos / tile_world_size * texture_size) + 0.5;
	return texel / texture_size;
}

// ── Vertex ────────────────────────────────────────────────────────────────────
void vertex() {
	// World-space XZ of this vertex.
	vec4 world_pos = MODEL_MATRIX * vec4(VERTEX, 1.0);
	vec2 world_xz  = world_pos.xz;

	// Snap UV to texel centre before sampling.
	vec2 uv = snap_uv(world_xz);
	vec4 tex = texture(cloud_texture, uv);

	float lum = dot(tex.rgb, vec3(0.299, 0.587, 0.114));
	v_density  = step(alpha_threshold, lum);
	v_world_uv = world_xz;

	// Extrude upward proportional to cloud density.
	VERTEX.y += v_density * extrude_height;
}

// ── Fragment ──────────────────────────────────────────────────────────────────
void fragment() {
	// Re-sample at the snapped UV to discard non-cloud pixels.
	vec2 uv  = snap_uv(v_world_uv);
	vec4 tex = texture(cloud_texture, uv);
	float lum = dot(tex.rgb, vec3(0.299, 0.587, 0.114));

	if (lum < alpha_threshold) {
		discard;
	}

	ALBEDO = cloud_color.rgb;
	ALPHA  = cloud_color.a;
}
