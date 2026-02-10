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
	
	public LightSceneObject()
	{
		ObjectType = "Point Light";
		
		// Create the OmniLight3D
		Light = new OmniLight3D();
		Light.Name = "OmniLight";
		Light.ShadowEnabled = false; // Shadows disabled for now
		Light.LightColor = _lightColor;
		Light.OmniRange = 5.0f; // Default range
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
	}
	
	public void SetRenderMode(bool enabled)
	{
		_renderModeEnabled = enabled;
		if (Light != null)
		{
			Light.Visible = enabled;
		}
	}
}
