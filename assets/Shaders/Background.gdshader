shader_type spatial;

render_mode shadows_disabled, specular_disabled, unshaded, fog_disabled, cull_disabled, skip_vertex_transform, depth_draw_never;

uniform sampler2D albedo_tex : source_color;
uniform bool stretch = true;

void vertex() {
	if (stretch) {
		POSITION = vec4(VERTEX.xy, 0.0, 1.0);
	}
	else {
		// Size the quad to match the albedo texture in pixels,
		// with the top-left corner anchored to the top-left of the viewport.
		vec2 tex_size = vec2(textureSize(albedo_tex, 0));
		// Convert texture pixel size to NDC extents (viewport spans -1..1, so width = 2.0)
		vec2 ndc_size = tex_size / VIEWPORT_SIZE * 2.0;
		// VERTEX.xy is in [-1, 1] for a size-2 quad.
		// Normalize to [0, 1]: t = (VERTEX.xy + 1.0) / 2.0
		vec2 t = (VERTEX.xy + 1.0) * 0.5; // (0,0) = bottom-left vert, (1,1) = top-right vert
		// Map x: left edge at NDC -1, right edge at NDC (-1 + ndc_size.x)
		float ndc_x = -1.0 + t.x * ndc_size.x;
		// Godot uses Y-down clip space (NDC y=-1 is top, y=1 is bottom).
		// t.y=1 is the top-right vertex in 3D, which maps to screen top (NDC y=-1).
		// t.y=0 is the bottom-left vertex in 3D, which maps to screen bottom (NDC y=1).
		// Top-left anchor: top edge at NDC y=-1, bottom edge at NDC y=(-1 + ndc_size.y)
		float ndc_y = -1.0 + (1.0 - t.y) * ndc_size.y;
		POSITION = vec4(ndc_x, ndc_y, 0.0, 1.0);
	}
}

void fragment() {
	// Flip UV vertically
    vec2 flipped_uv = vec2(UV.x, 1.0 - UV.y);
	
	// Sample the texture using the UV coordinates
    vec4 tex = texture(albedo_tex, UV);
	
	if (stretch) {
		tex = texture(albedo_tex, flipped_uv);
	}
	
	// Apply the color and alpha
    ALBEDO = tex.rgb;
    ALPHA = tex.a;
}
