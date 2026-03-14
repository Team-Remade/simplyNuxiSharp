using Godot;

namespace simplyRemadeNuxi.core;

public partial class LightSceneObject : SceneObject
{
	public OmniLight3D Light { get; private set; }
	public Sprite3D Sprite { get; private set; }
	
	private Color _lightColor = Colors.White;
	private bool _renderModeEnabled = false;

	public Color LightColor
	{
		get => _lightColor;
		set
		{
			_lightColor = value;
			if (Light != null)
			{
				Light.LightColor = value;
			}
			if (Sprite != null)
			{
				Sprite.Modulate = value;
			}
		}
	}

	/// <summary>
	/// Light energy (intensity/brightness). Default is 1.0.
	/// </summary>
	public float LightEnergy
	{
		get => Light?.LightEnergy ?? 1.0f;
		set
		{
			if (Light != null)
				Light.LightEnergy = value;
		}
	}

	/// <summary>
	/// Omni light range (radius). Default is 5.0.
	/// </summary>
	public float LightRange
	{
		get => Light?.OmniRange ?? 5.0f;
		set
		{
			if (Light != null)
				Light.OmniRange = value;
		}
	}

	/// <summary>
	/// Whether the light casts shadows.
	/// </summary>
	public bool LightShadowEnabled
	{
		get => Light?.ShadowEnabled ?? true;
		set
		{
			if (Light != null)
				Light.ShadowEnabled = value;
		}
	}

	/// <summary>
	/// Light indirect energy multiplier (affects global illumination contribution).
	/// </summary>
	public float LightIndirectEnergy
	{
		get => Light?.LightIndirectEnergy ?? 1.0f;
		set
		{
			if (Light != null)
				Light.LightIndirectEnergy = value;
		}
	}

	/// <summary>
	/// Light specular contribution (0 = no specular, 1 = full specular).
	/// </summary>
	public float LightSpecular
	{
		get => Light?.LightSpecular ?? 0.5f;
		set
		{
			if (Light != null)
				Light.LightSpecular = value;
		}
	}

	public LightSceneObject()
	{
		ObjectType = "Point Light";
		
		// Create the OmniLight3D
		Light = new OmniLight3D();
		Light.Name = "OmniLight";
		Light.ShadowEnabled = true;
		Light.LightColor = _lightColor;
		Light.OmniRange = 5.0f; // Default range
		Light.LightEnergy = 1.0f; // Default energy
		Light.LightIndirectEnergy = 1.0f;
		Light.LightSpecular = 0.5f;
		// Visibility will be set based on current render mode state
		AddVisualInstance(Light);
		
		// Create the sprite to visualize the light
		Sprite = new Sprite3D();
		Sprite.Name = "Sprite";
		Sprite.Texture = GD.Load<Texture2D>("res://assets/img/sprite/circle.svg");
		Sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled; // Make it face the camera
		Sprite.Shaded = false; // Make it unshaded so it's always visible
		Sprite.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear;
		Sprite.Modulate = _lightColor;
		
		// Set the sprite to only render on cull layer 2
		Sprite.Layers = 1 << 1; // Layer 2 (bit 1)
		
		// Scale the sprite to a reasonable size (0.5 units = 8 pixels in the 16-pixel scale)
		Sprite.Scale = new Vector3(0.5f, 0.5f, 0.5f);
		
		AddVisualInstance(Sprite);
		
		// Set pivot offset to zero for lights
		PivotOffset = Vector3.Zero;
	}
	
	public override void _Ready()
	{
		base._Ready();
		
		// Set initial visibility based on Main's render mode state
		if (simplyRemadeNuxi.Main.Instance != null)
		{
			SetRenderMode(simplyRemadeNuxi.Main.Instance.IsRenderModeEnabled());
		}
		else
		{
			// Default to disabled if Main instance not yet available
			SetRenderMode(false);
		}

		Sprite.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}
	
	public void SetRenderMode(bool enabled)
	{
		_renderModeEnabled = enabled;
		if (Light != null)
		{
			Light.Visible = enabled;
		}
		if (Sprite != null)
		{
			Sprite.Visible = !enabled;
		}
	}
}
