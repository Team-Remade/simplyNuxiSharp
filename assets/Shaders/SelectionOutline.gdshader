shader_type spatial;
render_mode unshaded, depth_test_disabled, wireframe, shadows_disabled;

uniform vec3 outline_color: source_color = vec3(0.043, 0.282, 0.467);

void fragment() {
	ALBEDO = outline_color;
}