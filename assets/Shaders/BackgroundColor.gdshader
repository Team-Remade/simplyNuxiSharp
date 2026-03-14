shader_type spatial;

uniform vec4 abledo_color : source_color;

render_mode shadows_disabled, specular_disabled, unshaded, fog_disabled, cull_disabled, skip_vertex_transform, depth_draw_never;

void vertex() {
	POSITION = vec4(VERTEX.xy, 0.0, 1.0);
}

void fragment() {
	ALBEDO = abledo_color.rgb;
}
