shader_type sky;
render_mode use_quarter_res_pass;

#define H(p)  fract(sin(mod(dot(p, vec2(12.9898, 78.233)),6.283)) * 43758.5453)

const float EPS = 1e-6;
const float INFINITY = 1.0 / 0.0;
const float PLANET_RADIUS = 6371000.0;
const vec3 PLANET_CENTER = vec3(0.0, -PLANET_RADIUS, 0.0);
const float ATMOSPHERE_HEIGHT = 100000.0;
const float RAYLEIGH_HEIGHT = (ATMOSPHERE_HEIGHT * 0.08);
const float MIE_HEIGHT = (ATMOSPHERE_HEIGHT * 0.012);

uniform bool use_debanding = true;

// ATMOSPHERE UNIFORMS
group_uniforms Atmosphere;
uniform int atmosphere_sample_count = 64;

uniform float elevation = 0.0;

uniform float rayleigh_strength : hint_range(0.0, 5.0) = 1.0;
uniform float mie_strength      : hint_range(0.0, 5.0) = 1.0;
uniform float ozone_strength    : hint_range(0.0, 5.0) = 1.0;

const vec3 C_RAYLEIGH = vec3(5.802, 13.558, 33.100) * 1e-6;
const vec3 C_MIE = vec3(3.996,  3.996,  3.996) * 1e-6;
const vec3 C_OZONE = vec3(0.650,  1.881,  0.085) * 1e-6;

uniform float mie_anisotropy_factor = 0.75;

uniform float atmosphere_density: hint_range(0.0, 5.0) = 1.0;
uniform float exposure = 10.0;

uniform float sun_disc_feather: hint_range(0.0, 1.0) = 0.5;
uniform float sundisc_intensity = 100.0;

// STAR UNIFORMS
group_uniforms Stars;
uniform samplerCube stars_hdr : filter_linear, source_color, hint_default_black;
uniform float stars_exposure: hint_range(0.0, 10.0) = 5.0;
uniform mat3 stars_rotation = mat3(1.0);
uniform bool dim_stars_at_day = true;

#define MAX_DIST 10000.0
#define MAX_MARCHES 64
#define SURF_DIST .01

// MOON UNIFORMS
group_uniforms Moons;
uniform float moon_dist[3];
uniform float moon_uv_x_offset[3];
uniform sampler2D moon_textures[3];

// SKY COLOR TINT UNIFORMS (same as MinecraftSky)
group_uniforms SkyColors;
uniform vec4 horizon_day_color    : source_color = vec4(0.576, 0.608, 1.0,  1.0);
uniform vec4 zenith_day_color     : source_color = vec4(0.12,  0.25,  0.55, 1.0);
uniform vec4 horizon_sunset_color : source_color = vec4(1.0,   0.45,  0.2,  1.0);
uniform vec4 zenith_sunset_color  : source_color = vec4(0.3,   0.1,   0.35, 1.0);
uniform vec4 night_horizon_color  : source_color = vec4(0.05,  0.05,  0.15, 1.0);
uniform vec4 night_zenith_color   : source_color = vec4(0.01,  0.01,  0.03, 1.0);
uniform vec4 stars_color          : source_color = vec4(1.0,   1.0,   1.0,  1.0);

group_uniforms Rainbow;
uniform bool use_rainbow = false;
uniform sampler2D rainbow_gradient: hint_default_white, filter_linear;
uniform float rainbow_width = .05;
uniform float rainbow_offset = .65;
uniform float rainbow_opacity: hint_range(0.0, 1.0) = .1;
uniform bool double_rainbow = true;


#define saturate(a) clamp(a, 0.0, 1.0)

vec2 sphere_intersection (vec3 rayStart, vec3 rayDir, vec3 sphereCenter, float sphereRadius)
{
	rayStart -= sphereCenter;
	float a = dot(rayDir, rayDir);
	float b = 2.0 * dot(rayStart, rayDir);
	float c = dot(rayStart, rayStart) - (sphereRadius * sphereRadius);
	float d = b * b - 4.0 * a * c;
	if (d < 0.0)
	{
		return vec2(-1.0);
	}
	else
	{
		d = sqrt(d);
		return vec2(-b - d, -b + d) / (2.0 * a);
	}
}

vec2 planet_intersection (vec3 rayStart, vec3 rayDir)
{
	return sphere_intersection(rayStart, rayDir, PLANET_CENTER, PLANET_RADIUS);
}

vec2 atmosphere_intersection (vec3 rayStart, vec3 rayDir)
{
	return sphere_intersection(rayStart, rayDir, PLANET_CENTER, PLANET_RADIUS + ATMOSPHERE_HEIGHT);
}

float phase_rayleigh (float costh)
{
	return 3.0 * (1.0 + costh*costh) / (16.0 * PI);
}

float phase_mie (float costh)
{
	float g = min(mie_anisotropy_factor, 0.9381);
	float k = 1.55*g - 0.55*g*g*g;
	float kcosth = k*costh;
	return (1.0 - k*k) / ((4.0 * PI) * (1.0-kcosth) * (1.0-kcosth));
}

float atmosphere_height (vec3 position_world_space) {
	return distance(position_world_space, PLANET_CENTER) - PLANET_RADIUS;
}
float density_rayleigh (float h) {
	return exp(-max(0.0, h / RAYLEIGH_HEIGHT));
}
float density_mie (float h) {
	return exp(-max(0.0, h / MIE_HEIGHT));
}
float density_ozone (float h) {
	return max(0.0, 1.0 - abs(h - 25000.0) / 15000.0);
}
vec3 density_atmosphere (float h) {
	return vec3(density_rayleigh(h), density_mie(h), density_ozone(h));
}

vec3 integrate_optical_depth (vec3 rayStart, vec3 rayDir)
{
	vec2 intersection = atmosphere_intersection(rayStart, rayDir);
	float  rayLength    = intersection.y;

	int    sampleCount  = 8;
	float  stepSize     = rayLength / float(sampleCount);

	vec3 opticalDepth = vec3(0.0);

	for (int i = 0; i < sampleCount; i++)
	{
		vec3 localPosition = rayStart + rayDir * (float(i) + 0.5) * stepSize;
		float localHeight  = atmosphere_height(localPosition);
		vec3 localDensity  = density_atmosphere(localHeight);

		opticalDepth += localDensity * stepSize;
	}

	return opticalDepth;
}

vec3 absorb (vec3 opticalDepth) {
	return exp(-(
		opticalDepth.x * C_RAYLEIGH * rayleigh_strength +
		opticalDepth.y * C_MIE * mie_strength +
		opticalDepth.z * C_OZONE * ozone_strength
	) * atmosphere_density);
}

vec3 integrate_scattering (vec3 rayStart, vec3 rayDir, float rayLength, vec3 lightDir, vec3 lightColor, out vec3 transmittance)
{
	float  rayHeight = atmosphere_height(rayStart);
	float  sampleDistributionExponent = 1.0 + saturate(1.0 - rayHeight / ATMOSPHERE_HEIGHT) * 8.0;

	vec2 intersection = atmosphere_intersection(rayStart, rayDir);
	rayLength = min(rayLength, intersection.y);
	if (intersection.x > 0.0)
	{
		rayStart += rayDir * intersection.x;
		rayLength -= intersection.x;
	}

	float  costh    = dot(rayDir, lightDir);
	float  phaseR   = phase_rayleigh(costh);
	float  phaseM   = phase_mie(costh);

	vec3 opticalDepth = vec3(0.0);
	vec3 rayleigh     = vec3(0.0);
	vec3 mie          = vec3(0.0);

	float  prevRayTime  = 0.0;

	for (int i = 0; i < atmosphere_sample_count; i++)
	{
		float  rayTime = pow(float(i) / float(atmosphere_sample_count), sampleDistributionExponent) * rayLength;
		float  stepSize = (rayTime - prevRayTime);

		vec3 localPosition = rayStart + rayDir * rayTime;
		float  localHeight   = atmosphere_height(localPosition);
		vec3 localDensity  = density_atmosphere(localHeight);

		opticalDepth += localDensity * stepSize;

		vec3 viewTransmittance = absorb(opticalDepth);

		vec3 opticalDepthlight  = integrate_optical_depth(localPosition, lightDir);
		vec3 lightTransmittance = absorb(opticalDepthlight);

		rayleigh += viewTransmittance * lightTransmittance * phaseR * localDensity.x * stepSize;
		mie      += viewTransmittance * lightTransmittance * phaseM * localDensity.y * stepSize;

		prevRayTime = rayTime;
	}

	transmittance = absorb(opticalDepth);

	return (rayleigh * C_RAYLEIGH * rayleigh_strength + mie * C_MIE * mie_strength) * lightColor * exposure;
}

float sun_disc(vec3 eyedir, vec3 sundir, float theta_r) {
	float cos_angle = dot(eyedir, sundir);
	float cos_inner = cos(theta_r * (1.0 - sun_disc_feather));
	float cos_outer = cos(theta_r * (1.0 + sun_disc_feather));
	return smoothstep(cos_outer, cos_inner, cos_angle);
}

float rand(vec2 co){
    return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}

float sdf_sphere(vec3 ray_origin, vec3 sphere_origin, float sphere_radius) {
	return  length(sphere_origin - ray_origin) - sphere_radius;
}

struct Hit {
	float dist;
	int id;
};

Hit get_dist(vec3 marched_ray) {
	Hit closest = Hit(1e20, -1);

	if (LIGHT1_ENABLED) {
		vec3 moon_pos = LIGHT1_DIRECTION * moon_dist[0];
		float dist = sdf_sphere(marched_ray, moon_pos, LIGHT1_SIZE * 100.0);
		if (dist < closest.dist) {
			closest.dist = dist;
			closest.id = 0;
		}
	}
	if (LIGHT2_ENABLED) {
		vec3 moon_pos = LIGHT2_DIRECTION * moon_dist[1];
		float dist = sdf_sphere(marched_ray, moon_pos, LIGHT2_SIZE * 100.0);
		if (dist < closest.dist) {
			closest.dist = dist;
			closest.id = 1;
		}
	}
	if (LIGHT3_ENABLED) {
		vec3 moon_pos = LIGHT3_DIRECTION * moon_dist[2];
		float dist = sdf_sphere(marched_ray, moon_pos, LIGHT3_SIZE * 100.0);
		if (dist < closest.dist) {
			closest.dist = dist;
			closest.id = 2;
		}
	}

	return closest;
}

Hit raymarch_moons(vec3 ray_direction) {
	Hit distance_so_far = Hit(0.0, 0);

	for (int i = 0; i < MAX_MARCHES; i++) {
		vec3 marched_ray = ray_direction * distance_so_far.dist;
		Hit distance_to_scene = get_dist(marched_ray);
		distance_so_far.dist += distance_to_scene.dist;
		distance_so_far.id = distance_to_scene.id;
		if (distance_to_scene.dist < SURF_DIST || distance_so_far.dist > MAX_DIST) {
			break;
		}
	}

	return distance_so_far;
}

vec3 get_normal(vec3 point) {
	float dist = get_dist(point).dist;

	vec3 normal = dist - vec3(
		get_dist(point - vec3(0.01, 0.0, 0.0)).dist,
		get_dist(point - vec3(0.0, 0.01, 0.0)).dist,
		get_dist(point - vec3(0.0, 0.0, 0.01)).dist
	);

	return normalize(normal);
}

float shade(vec3 light_dir, vec3 point) {
	vec3 normal = get_normal(point);

	float shade = clamp(dot(normal, light_dir), 0.0, 1.0);
	return shade;
}

vec2 spherical_uv_projection(vec3 normal, float shift) {
    float u = 0.5 + atan(normal.z, normal.x) / (2.0 * PI) + shift;
    float v = 0.5 - asin(normal.y) / PI;
    return vec2(u, v);
}

vec3 rainbow(vec3 eyedir, vec3 transmittance) {
	vec3 res = vec3(0.0);
	// Main rainbow
	{
		float lower_bound = rainbow_offset - rainbow_width;
		float upper_bound = rainbow_offset + rainbow_width;
		float lower_mask = smoothstep(lower_bound, lower_bound + .02, eyedir.x);
		float upper_mask = smoothstep(upper_bound, upper_bound - .02, eyedir.x);
		res += texture(
				rainbow_gradient,
				vec2(smoothstep(lower_bound, upper_bound, eyedir.x), 0.0)
			).rgb
			* lower_mask
			* upper_mask
			* rainbow_opacity
			* transmittance;
	}

	// Double rainbow
	if (double_rainbow) {
		float lower_bound = rainbow_offset * 0.9 - rainbow_width * 1.5;
		float upper_bound = rainbow_offset * 0.9 + rainbow_width * 1.5;
		float lower_mask = smoothstep(lower_bound, lower_bound + .02, eyedir.x);
		float upper_mask = smoothstep(upper_bound, upper_bound - .02, eyedir.x);
		res += texture(
				rainbow_gradient,
				vec2(smoothstep(lower_bound, upper_bound, eyedir.x), 0.0)
			).rgb
			* lower_mask
			* upper_mask
			* rainbow_opacity * .15
			* transmittance;
	}

	return res;
}

void sky() {
	if (AT_CUBEMAP_PASS) {
		vec3 transmittance;
		COLOR = integrate_scattering(vec3(0.0, elevation, 0.0), EYEDIR, INFINITY, LIGHT0_DIRECTION, vec3(1.0, 0.996, 0.98), transmittance);
	} else if (AT_QUARTER_RES_PASS) {
		vec3 transmittance;
		integrate_scattering(vec3(0.0, elevation, 0.0), EYEDIR, INFINITY, LIGHT0_DIRECTION, vec3(1.0, 0.996, 0.98), transmittance);
		COLOR = transmittance;
	} else {
		vec3 transmittance = QUARTER_RES_COLOR.rgb;
		vec3 radiance = texture(RADIANCE, EYEDIR).rgb;

		// ── Sky color tint (same gradient logic as MinecraftSky) ─────────────
		float sun_elev      = LIGHT0_DIRECTION.y;
		float day_factor    = clamp(sun_elev * 2.0 + 0.5, 0.0, 1.0);
		float sunset_factor = pow(clamp(1.0 - abs(sun_elev) * 3.0, 0.0, 1.0), 2.0);
		float night_factor  = clamp(-sun_elev * 2.0 + 0.5, 0.0, 1.0);
		float horiz_blend   = pow(clamp(abs(EYEDIR.y) * 2.0, 0.0, 1.0), 0.5);

		vec3 day_sky    = mix(horizon_day_color.rgb,    zenith_day_color.rgb,    horiz_blend);
		vec3 sunset_sky = mix(horizon_sunset_color.rgb, zenith_sunset_color.rgb, horiz_blend);
		vec3 night_sky  = mix(night_horizon_color.rgb,  night_zenith_color.rgb,  horiz_blend);

		vec3 sky_tint = mix(night_sky, day_sky, day_factor);
		sky_tint      = mix(sky_tint, sunset_sky, sunset_factor);

		// Multiply the physical radiance by the color tint
		radiance *= sky_tint;

		float sun_mask = sun_disc(EYEDIR, LIGHT0_DIRECTION, LIGHT0_SIZE * 0.5);
		vec3 sundisc = vec3(sun_mask) * transmittance * sundisc_intensity;

		vec3 stars =
			texture(stars_hdr, EYEDIR * stars_rotation).rgb *
			stars_exposure *
			transmittance *
			stars_color.rgb;

		if (dim_stars_at_day) {
			float stars_mult = smoothstep(
				-0.1,
				0.3,
				dot(LIGHT0_DIRECTION, vec3(0.0, -1.0, 0.0))
			);
			stars *= stars_mult;
		}

		// Moon pass
		float moon_mask = 1.0;
		vec3 moon_diff = vec3(0.0);

		Hit moon_hit = raymarch_moons(EYEDIR);

		if (
			moon_hit.dist < MAX_DIST &&
			get_dist(EYEDIR * moon_hit.dist).dist < SURF_DIST * 128.0
		) {
			moon_mask = 0.0;

			moon_diff = texture(
				moon_textures[moon_hit.id],
				spherical_uv_projection(get_normal(EYEDIR * moon_hit.dist), moon_uv_x_offset[moon_hit.id])
			).rgb;

			moon_diff *= shade(LIGHT0_DIRECTION, EYEDIR * moon_hit.dist);

			switch (moon_hit.id) {
				case 0:
					moon_diff *= LIGHT1_COLOR * LIGHT1_ENERGY;
					break;
				case 1:
					moon_diff *= LIGHT2_COLOR * LIGHT2_ENERGY;
					break;
				case 3:
					moon_diff *= LIGHT3_COLOR * LIGHT3_ENERGY;
					break;
			}

			moon_diff *= transmittance;
		}

		COLOR = radiance + (sundisc + stars) * moon_mask + moon_diff;

		// Rainbows
		if (use_rainbow) {
			COLOR += rainbow(EYEDIR, transmittance);
		}

		if (use_debanding) {
			COLOR += (rand(SKY_COORDS) - 0.5) * 0.001;
		}
	}
}
