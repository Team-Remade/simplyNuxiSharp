shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque;

uniform vec4 pick_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	ALBEDO = pick_color.rgb;
	ALPHA = pick_color.a;
}
