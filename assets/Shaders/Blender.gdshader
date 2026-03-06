shader_type spatial;

uniform vec4 albedo_color : source_color = vec4(0.8, 0.8, 0.8, 1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white;
uniform bool use_albedo_texture = false;

uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D metallic_texture : hint_default_white;
uniform bool use_metallic_texture = false;

uniform float roughness : hint_range(0.0, 1.0) = 0.5;
uniform sampler2D roughness_texture : hint_default_white;
uniform bool use_roughness_texture = false;

uniform sampler2D normal_texture : hint_normal;
uniform bool use_normal_texture = false;
uniform float normal_scale : hint_range(-16.0, 16.0) = 1.0;

uniform sampler2D emission_texture : source_color, hint_default_black;
uniform bool use_emission_texture = false;
uniform vec3 emission_color : source_color = vec3(0.0, 0.0, 0.0);
uniform float emission_energy : hint_range(0.0, 16.0) = 1.0;

uniform float alpha_scissor_threshold : hint_range(0.0, 1.0) = 0.5;
uniform bool use_alpha_scissor = false;

void fragment() {
	vec4 base_color = albedo_color;
	if (use_albedo_texture) {
		base_color *= texture(albedo_texture, UV);
	}

	if (use_alpha_scissor && base_color.a < alpha_scissor_threshold) {
		discard;
	}

	ALBEDO = base_color.rgb;
	ALPHA = base_color.a;

	float metal = metallic;
	if (use_metallic_texture) {
		metal *= texture(metallic_texture, UV).r;
	}
	METALLIC = metal;

	float rough = roughness;
	if (use_roughness_texture) {
		rough *= texture(roughness_texture, UV).g;
	}
	ROUGHNESS = rough;

	if (use_normal_texture) {
		vec3 n = texture(normal_texture, UV).rgb;
		NORMAL_MAP = n;
		NORMAL_MAP_DEPTH = normal_scale;
	}

	vec3 emit = emission_color * emission_energy;
	if (use_emission_texture) {
		emit += texture(emission_texture, UV).rgb * emission_energy;
	}
	EMISSION = emit;
}