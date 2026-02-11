using Godot;
using System;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Manages animated textures for Minecraft-style vertical sprite sheet animations
/// This class creates a material with a shader that animates through frames based on mcmeta data
/// </summary>
public class AnimatedTextureMaterial
{
	private static Shader _animationShader;
	
	/// <summary>
	/// Global animation speed multiplier (default: 1.0)
	/// </summary>
	public static float GlobalAnimationSpeed { get; set; } = 1.0f;
	
	/// <summary>
	/// Global animation time (controlled by timeline)
	/// </summary>
	public static float GlobalAnimationTime { get; set; } = 0.0f;
	
	/// <summary>
	/// Texture animation framerate (default: 20 fps as per Minecraft)
	/// </summary>
	public static float TextureAnimationFps { get; set; } = 20.0f;
	
	/// <summary>
	/// Gets or creates the animation shader
	/// </summary>
	private static Shader GetAnimationShader()
	{
		if (_animationShader != null)
			return _animationShader;
			
		_animationShader = new Shader();
		_animationShader.Code = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_disabled;

uniform sampler2D texture_albedo : source_color, filter_nearest, repeat_disable;
uniform float frame_count = 1.0;
uniform float frame_time = 1.0; // Time per frame in seconds
uniform bool interpolate = false;
uniform float animation_time = 0.0; // Custom animation time controlled externally

void fragment() {
	// Calculate current frame based on custom animation time
	float total_time = animation_time;
	float frame_duration = frame_time;
	int current_frame = int(floor(total_time / frame_duration)) % int(frame_count);
	
	// Calculate UV offset for vertical sprite sheet
	float frame_height = 1.0 / frame_count;
	float v_offset = float(current_frame) * frame_height;
	
	// Adjust UV coordinates
	vec2 anim_uv = vec2(UV.x, UV.y * frame_height + v_offset);
	
	// Sample texture
	vec4 albedo_tex = texture(texture_albedo, anim_uv);
	
	ALBEDO = albedo_tex.rgb;
	ALPHA = albedo_tex.a;
	
	// Alpha scissor
	if (ALPHA < 0.5) {
		discard;
	}
}
";
		
		return _animationShader;
	}
	
	/// <summary>
	/// Creates a material for an animated texture based on mcmeta data
	/// </summary>
	/// <param name="texture">The texture to animate (vertical sprite sheet)</param>
	/// <param name="metadata">Animation metadata from .mcmeta file</param>
	/// <returns>A material configured for animation</returns>
	public static ShaderMaterial CreateAnimatedMaterial(ImageTexture texture, MinecraftTextureMetadata metadata)
	{
		if (texture == null || metadata?.Animation == null)
			return null;
			
		var material = new ShaderMaterial();
		material.Shader = GetAnimationShader();
		
		// Set the texture
		material.SetShaderParameter("texture_albedo", texture);
		
		// Calculate frame count
		int frameCount = CalculateFrameCount(texture, metadata.Animation);
		material.SetShaderParameter("frame_count", (float)frameCount);
		
		// Convert Minecraft ticks to seconds using the texture animation fps setting
		// Minecraft default: 20 ticks = 1 second, frametime is in ticks
		// So frame_time_seconds = frametime / TextureAnimationFps
		// Apply global animation speed multiplier
		float frameTimeSeconds = (metadata.Animation.Frametime / TextureAnimationFps) / GlobalAnimationSpeed;
		material.SetShaderParameter("frame_time", frameTimeSeconds);
		
		// Set interpolation flag
		material.SetShaderParameter("interpolate", metadata.Animation.Interpolate);
		
		// Initialize animation time to global time
		material.SetShaderParameter("animation_time", GlobalAnimationTime);
		
		// Register the material with the manager
		AnimatedTextureManager.Instance?.RegisterMaterial(material);
		
		GD.Print($"Created animated material: {frameCount} frames, {frameTimeSeconds}s per frame, interpolate={metadata.Animation.Interpolate}");
		
		return material;
	}
	
	/// <summary>
	/// Updates all animated materials with the current global animation time
	/// Call this every frame while the timeline is playing
	/// </summary>
	public static void UpdateAnimationTime(float deltaTime)
	{
		// Increment global animation time
		GlobalAnimationTime += deltaTime * GlobalAnimationSpeed;
	}
	
	/// <summary>
	/// Resets the global animation time to zero
	/// </summary>
	public static void ResetAnimationTime()
	{
		GlobalAnimationTime = 0.0f;
	}
	
	/// <summary>
	/// Updates a material's framerate based on the current TextureAnimationFps
	/// Called when texture animation FPS setting changes
	/// </summary>
	public static void UpdateMaterialFramerate(ShaderMaterial material)
	{
		if (material == null) return;
		
		// Get current frame_time parameter (this was calculated with old FPS)
		var currentFrameTime = material.GetShaderParameter("frame_time").AsSingle();
		
		// We need to recalculate based on the metadata, but we don't have access to it here
		// So we'll just store the frame_time and update when needed
		// For now, materials will be updated on next creation
		// This is mainly for future materials
	}
	
	/// <summary>
	/// Calculates the number of frames in the texture based on metadata and texture dimensions
	/// </summary>
	private static int CalculateFrameCount(ImageTexture texture, AnimationData animation)
	{
		// If custom frames are defined, use their count
		if (animation.Frames != null && animation.Frames.Count > 0)
		{
			return animation.Frames.Count;
		}
		
		// Calculate from texture dimensions
		var image = texture.GetImage();
		if (image == null)
			return 1;
			
		int textureWidth = image.GetWidth();
		int textureHeight = image.GetHeight();
		
		// Frame height defaults to texture width (for square frames)
		int frameHeight = animation.Height ?? textureWidth;
		
		// Calculate number of frames
		int frameCount = textureHeight / frameHeight;
		
		return Math.Max(1, frameCount);
	}
	
	/// <summary>
	/// Creates a standard non-animated material (fallback)
	/// </summary>
	public static StandardMaterial3D CreateStandardMaterial(ImageTexture texture)
	{
		var material = new StandardMaterial3D();
		
		if (texture != null)
		{
			material.AlbedoColor = Colors.White;
			material.AlbedoTexture = texture;
			material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
			material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
			material.AlphaScissorThreshold = 0.5f;
			return material;
		}
		
		// Fallback material
		material.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
		return material;
	}
}
