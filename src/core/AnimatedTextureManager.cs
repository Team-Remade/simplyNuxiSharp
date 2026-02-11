using Godot;
using System.Collections.Generic;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Manages all animated texture materials in the scene
/// Updates shader parameters when timeline plays
/// </summary>
public partial class AnimatedTextureManager : Node
{
	private static AnimatedTextureManager _instance;
	public static AnimatedTextureManager Instance => _instance;
	
	private List<ShaderMaterial> _animatedMaterials = new List<ShaderMaterial>();
	private bool _isPlaying = false;
	
	public float AnimationSpeed { get; set; } = 1.0f;
	
	/// <summary>
	/// Texture animation framerate (default 20 fps as per Minecraft)
	/// </summary>
	public float TextureAnimationFps { get; set; } = 20.0f;
	
	public override void _EnterTree()
	{
		_instance = this;
		AnimatedTextureMaterial.GlobalAnimationSpeed = AnimationSpeed;
	}
	
	public override void _ExitTree()
	{
		_instance = null;
	}
	
	public override void _Process(double delta)
	{
		if (_isPlaying)
		{
			// Update global animation time with delta
			AnimatedTextureMaterial.UpdateAnimationTime((float)delta);
			
			// Update all registered materials with the current animation time
			UpdateMaterials();
		}
	}
	
	/// <summary>
	/// Updates all materials with the current animation time
	/// </summary>
	private void UpdateMaterials()
	{
		foreach (var material in _animatedMaterials)
		{
			if (material != null)
			{
				material.SetShaderParameter("animation_time", AnimatedTextureMaterial.GlobalAnimationTime);
			}
		}
	}
	
	/// <summary>
	/// Sets the animation time directly (for timeline scrubbing)
	/// </summary>
	public void SetAnimationTime(float time)
	{
		AnimatedTextureMaterial.GlobalAnimationTime = time;
		UpdateMaterials();
	}
	
	/// <summary>
	/// Registers an animated material to be updated
	/// </summary>
	public void RegisterMaterial(ShaderMaterial material)
	{
		if (material != null && !_animatedMaterials.Contains(material))
		{
			_animatedMaterials.Add(material);
		}
	}
	
	/// <summary>
	/// Unregisters an animated material
	/// </summary>
	public void UnregisterMaterial(ShaderMaterial material)
	{
		_animatedMaterials.Remove(material);
	}
	
	/// <summary>
	/// Starts playing animations
	/// </summary>
	public void Play()
	{
		_isPlaying = true;
	}
	
	/// <summary>
	/// Stops playing animations
	/// </summary>
	public void Stop()
	{
		_isPlaying = false;
	}
	
	/// <summary>
	/// Pauses animations
	/// </summary>
	public void Pause()
	{
		_isPlaying = false;
	}
	
	/// <summary>
	/// Resets animation time
	/// </summary>
	public void Reset()
	{
		AnimatedTextureMaterial.ResetAnimationTime();
		
		// Update all materials to time zero
		foreach (var material in _animatedMaterials)
		{
			if (material != null)
			{
				material.SetShaderParameter("animation_time", 0.0f);
			}
		}
	}
	
	/// <summary>
	/// Sets the animation speed multiplier
	/// </summary>
	public void SetAnimationSpeed(float speed)
	{
		AnimationSpeed = speed;
		AnimatedTextureMaterial.GlobalAnimationSpeed = speed;
	}
	
	/// <summary>
	/// Sets the texture animation framerate
	/// </summary>
	public void SetTextureAnimationFps(float fps)
	{
		TextureAnimationFps = fps;
		AnimatedTextureMaterial.TextureAnimationFps = fps;
		
		// Update all existing materials with new framerate
		foreach (var material in _animatedMaterials)
		{
			if (material != null)
			{
				AnimatedTextureMaterial.UpdateMaterialFramerate(material);
			}
		}
	}
	
	/// <summary>
	/// Clears all registered materials
	/// </summary>
	public void Clear()
	{
		_animatedMaterials.Clear();
	}
}
