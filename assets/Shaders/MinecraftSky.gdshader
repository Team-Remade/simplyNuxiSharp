shader_type sky;
render_mode use_debanding;
// Minecraft-style sky shader
// Sun/moon direction is driven by LIGHT0_DIRECTION (the first directional light
// that has sky_mode enabled in the scene).  Rotating that light in the scene
// automatically moves the sun/moon and changes the sky gradient.
//
// Features:
//   - Day/night gradient (horizon → zenith) that follows the sun elevation
//   - Textured sun disc  (minecraft_sun.png)
//   - Textured moon disc (full_moon.png)
//   - Procedural stars at night (optionally driven by a noise texture)
//   - Void/dark colour below the horizon
//   - Clouds are handled by a separate 3D mesh (MinecraftClouds)
// ── Sky gradient colours ──────────────────────────────────────────────────────
uniform vec4 horizon_day_color    : source_color = vec4(0.576, 0.608, 1.0,  1.0);
uniform vec4 zenith_day_color     : source_color = vec4(0.12,  0.25,  0.55, 1.0);
uniform vec4 horizon_sunset_color : source_color = vec4(1.0,   0.45,  0.2,  1.0);
uniform vec4 zenith_sunset_color  : source_color = vec4(0.3,   0.1,   0.35, 1.0);
uniform vec4 night_horizon_color  : source_color = vec4(0.05,  0.05,  0.15, 1.0);
uniform vec4 night_zenith_color   : source_color = vec4(0.01,  0.01,  0.03, 1.0);
uniform vec4 stars_color          : source_color = vec4(1.0,   1.0,   1.0,  1.0);
// ── Sun / Moon textures ───────────────────────────────────────────────────────
uniform sampler2D sun_texture  : source_color, hint_default_white, filter_nearest;
uniform sampler2D moon_texture : source_color, hint_default_white, filter_nearest;
// ── Sun / Moon sizes (half-angle in radians) ──────────────────────────────────
uniform float sun_size  : hint_range(0.01, 0.5) = 0.05;
uniform float moon_size : hint_range(0.01, 0.5) = 0.04;
// ── Sun / Moon texture rotation (degrees, z-axis) ────────────────────────────
uniform float disc_rotation : hint_range(-180.0, 180.0) = 0.0;
// ── Stars ─────────────────────────────────────────────────────────────────────
// Optional noise texture for star placement.
// When set, the red channel is used as the star density map.
// Leave unset to use the built-in procedural hash noise.
uniform sampler2D star_noise_texture : hint_default_black, filter_linear;
uniform float star_noise_scale : hint_range(0.1, 10.0) = 1.0;
uniform float star_threshold   : hint_range(0.0, 1.0)  = 0.92;
uniform float star_brightness  : hint_range(0.0, 5.0)  = 1.0;
// ── Helpers ───────────────────────────────────────────────────────────────────
float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}
// Simple 2D value noise for procedural stars
float proc_star_noise(vec2 uv) {
	vec2 i = floor(uv);
	vec2 f = fract(uv);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}
// Build a local tangent frame around a direction vector
void tangent_frame(vec3 dir, out vec3 tangent, out vec3 bitangent) {
	vec3 up   = abs(dir.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
	tangent   = normalize(cross(up, dir));
	bitangent = normalize(cross(dir, tangent));
}
// Sample a textured disc centred on disc_dir with half-angle size_rad.
vec4 sample_disc(vec3 eye_dir, vec3 disc_dir, float size_rad,
                 sampler2D tex, float brightness, float rot_deg) {
	float cos_angle = dot(eye_dir, disc_dir);
	float cos_size  = cos(size_rad);
	if (cos_angle < cos_size - 0.002)
		return vec4(0.0);
	vec3 tang, bitang;
	tangent_frame(disc_dir, tang, bitang);
	vec3  offset = eye_dir - disc_dir * cos_angle;
	float u = dot(offset, tang)   / sin(size_rad);
	float v = dot(offset, bitang) / sin(size_rad);
	// Apply z-rotation to the disc UV
	float angle = radians(rot_deg);
	float cs = cos(angle);
	float sn = sin(angle);
	float ru = cs * u - sn * v;
	float rv = sn * u + cs * v;
	vec2 uv = clamp(vec2(ru, rv) * 0.5 + 0.5, 0.0, 1.0);
	vec4 col  = texture(tex, uv);
	// Make black pixels transparent: use luminance as alpha mask so the black
	// background of sun/moon textures does not occlude the sky behind them.
	float lum = dot(col.rgb, vec3(0.299, 0.587, 0.114));
	col.a *= lum;
	float edge = smoothstep(cos_size - 0.003, cos_size + 0.003, cos_angle);
	col.a  *= edge;
	col.rgb *= brightness;
	return col;
}
void sky() {
	// ── Sun direction from the first sky-affecting directional light ──────────
	// In Godot's sky shader, LIGHT0_DIRECTION is the direction TOWARD the light
	// source (i.e. the direction you look to see the sun).
	vec3 to_sun  = normalize(LIGHT0_DIRECTION);
	vec3 to_moon = -to_sun;   // moon is always opposite the sun
	// Sun elevation: +1 = overhead, -1 = below horizon
	float sun_elev = to_sun.y;
	// ── Time-of-day factors ───────────────────────────────────────────────────
	float day_factor    = clamp(sun_elev * 2.0 + 0.5, 0.0, 1.0);
	float sunset_factor = pow(clamp(1.0 - abs(sun_elev) * 3.0, 0.0, 1.0), 2.0);
	float night_factor  = clamp(-sun_elev * 2.0 + 0.5, 0.0, 1.0);
	// ── Sky gradient ──────────────────────────────────────────────────────────
	float elevation   = EYEDIR.y;
	float horiz_blend = pow(clamp(abs(elevation) * 2.0, 0.0, 1.0), 0.5);
	vec3 day_sky    = mix(horizon_day_color.rgb,    zenith_day_color.rgb,    horiz_blend);
	vec3 sunset_sky = mix(horizon_sunset_color.rgb, zenith_sunset_color.rgb, horiz_blend);
	vec3 night_sky  = mix(night_horizon_color.rgb,  night_zenith_color.rgb,  horiz_blend);
	vec3 sky_color = mix(night_sky, day_sky, day_factor);
	sky_color      = mix(sky_color, sunset_sky, sunset_factor);
	// ── Sun disc (textured) ───────────────────────────────────────────────────
	float sun_vis = clamp(day_factor * 2.0, 0.0, 1.0);
	if (sun_vis > 0.001) {
		vec4 sun_col = sample_disc(EYEDIR, to_sun, sun_size, sun_texture, 2.0, disc_rotation);
		sky_color = mix(sky_color, sun_col.rgb, sun_col.a * sun_vis);
	}
	// ── Moon disc (textured) ──────────────────────────────────────────────────
	float moon_vis = clamp(night_factor * 2.0, 0.0, 1.0);
	if (moon_vis > 0.001) {
		vec4 moon_col = sample_disc(EYEDIR, to_moon, moon_size, moon_texture, 1.5, disc_rotation);
		sky_color = mix(sky_color, moon_col.rgb, moon_col.a * moon_vis);
	}
	// ── Stars ─────────────────────────────────────────────────────────────────
	if (elevation > -0.05 && night_factor > 0.01) {
		// Spherical UV for star placement
		float star_u = atan(EYEDIR.z, EYEDIR.x) / TAU + 0.5;
		float star_v = asin(clamp(EYEDIR.y, -1.0, 1.0)) / PI + 0.5;
		vec2 star_uv = vec2(star_u, star_v) * star_noise_scale;
		float stars = 0.0;
		// Sample the noise texture if it has been assigned (non-black texel at 0,0)
		vec4 noise_sample = texture(star_noise_texture, star_uv);
		if (noise_sample.r > 0.001 || noise_sample.g > 0.001 || noise_sample.b > 0.001) {
			// Texture-driven stars: use red channel as density
			stars = step(star_threshold, noise_sample.r);
		} else {
			// Procedural fallback: multi-scale hash noise
			stars += step(0.92, proc_star_noise(star_uv * 80.0)) * 0.8;
			stars += step(0.95, proc_star_noise(star_uv * 40.0 + vec2(13.7, 5.3))) * 1.0;
			stars += step(0.97, proc_star_noise(star_uv * 20.0 + vec2(7.1, 19.4))) * 1.5;
			stars  = clamp(stars, 0.0, 1.5);
		}
		float fade = clamp(elevation * 4.0, 0.0, 1.0);
		sky_color += stars_color.rgb * stars * star_brightness * night_factor * fade;
	}
	// ── Void / fog below horizon ──────────────────────────────────────────────
	if (elevation < 0.0) {
		float void_blend = clamp(-elevation * 4.0, 0.0, 1.0);
		vec3 void_color  = mix(night_horizon_color.rgb, vec3(0.0), void_blend * 0.5);
		sky_color = mix(sky_color, void_color, void_blend);
	}
	COLOR = sky_color;
}