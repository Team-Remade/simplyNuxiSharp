using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace simplyRemadeNuxi.core;

public partial class ProjectPropertiesPanel : Panel
{
	private VBoxContainer _vboxContainer;
	private ColorPickerButton _backgroundColorPicker;
	private Button _backgroundImageButton;
	private Label _backgroundImageLabel;
	private CheckBox _stretchToFitCheckbox;
	private CollapsibleSection _backgroundSection;
	
	// Floor controls
	private CollapsibleSection _floorSection;
	private CheckBox _floorVisibilityCheckbox;
	private OptionButton _floorTextureDropdown;
	private Label _floorTextureLabel;
	
	// Reference to the background nodes in the viewport
	private ColorRect _backgroundColorNode;
	private TextureRect _backgroundImageNode;
	
	// Reference to the floor node
	private Node3D _floorNode;
	private MeshInstance3D _floorMeshInstance;
	
	// Store the current background image path
	private string _currentBackgroundImagePath = "";
	
	// Store available block textures
	private List<string> _blockTexturePaths;

	public override void _Ready()
	{
		SetupUi();
		FindBackgroundNodes();
		FindFloorNode();
		LoadCurrentBackgroundSettings();
		LoadBlockTextures();
		LoadCurrentFloorSettings();
	}

	private void SetupUi()
	{
		// Add ScrollContainer to handle overflow
		var scrollContainer = new ScrollContainer();
		scrollContainer.Name = "ScrollContainer";
		scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(scrollContainer);

		var vbox = new VBoxContainer();
		vbox.Name = "VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 4);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollContainer.AddChild(vbox);
		_vboxContainer = vbox;

		// Title label
		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel";
		titleLabel.Text = "Project Properties";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(titleLabel);

		// Add some spacing
		var spacer1 = new Control();
		spacer1.CustomMinimumSize = new Vector2(0, 10);
		vbox.AddChild(spacer1);

		// Background section with collapsible dropdown
		_backgroundSection = new CollapsibleSection("Background");
		_backgroundSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_backgroundSection);
		
		// Hide the reset button for project properties
		_backgroundSection.GetResetButton().Visible = false;
		
		var backgroundContainer = _backgroundSection.GetContentContainer();

		// Background Color
		var colorRow = new HBoxContainer();
		backgroundContainer.AddChild(colorRow);
		
		var colorLabel = new Label();
		colorLabel.Text = "Color:";
		colorLabel.CustomMinimumSize = new Vector2(60, 0);
		colorRow.AddChild(colorLabel);
		
		_backgroundColorPicker = new ColorPickerButton();
		_backgroundColorPicker.Name = "BackgroundColorPicker";
		_backgroundColorPicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_backgroundColorPicker.CustomMinimumSize = new Vector2(0, 32);
		_backgroundColorPicker.EditAlpha = true;
		_backgroundColorPicker.ColorChanged += OnBackgroundColorChanged;
		colorRow.AddChild(_backgroundColorPicker);

		// Add color presets row
		var presetsLabel = new Label();
		presetsLabel.Text = "Presets:";
		presetsLabel.CustomMinimumSize = new Vector2(60, 0);
		presetsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		presetsLabel.AddThemeFontSizeOverride("font_size", 11);
		backgroundContainer.AddChild(presetsLabel);
		
		var presetsRow = new HBoxContainer();
		presetsRow.AddThemeConstantOverride("separation", 4);
		backgroundContainer.AddChild(presetsRow);
		
		// Define color presets - sky-like colors
		var presets = new (string name, Color color)[]
		{
			("Dawn", new Color(1f, 0.7f, 0.5f, 1f)),           // Warm orange/pink
			("Morning", new Color(0.6f, 0.8f, 1f, 1f)),        // Light blue
			("Day", new Color(0.5764706f, 0.5764706f, 1f, 1f)), // Default sky blue
			("Sunset", new Color(1f, 0.5f, 0.3f, 1f)),         // Orange/red
			("Dusk", new Color(0.3f, 0.4f, 0.7f, 1f)),         // Purple/blue
			("Night", new Color(0.05f, 0.05f, 0.15f, 1f))      // Dark blue/black
		};
		
		foreach (var preset in presets)
		{
			var presetButton = new Button();
			presetButton.CustomMinimumSize = new Vector2(24, 24);
			presetButton.TooltipText = preset.name;
			
			// Create colored stylebox for the button
			var styleBox = new StyleBoxFlat();
			styleBox.BgColor = preset.color;
			styleBox.SetBorderWidthAll(1);
			styleBox.BorderColor = new Color(0.5f, 0.5f, 0.5f);
			
			presetButton.AddThemeStyleboxOverride("normal", styleBox);
			presetButton.AddThemeStyleboxOverride("hover", styleBox);
			presetButton.AddThemeStyleboxOverride("pressed", styleBox);
			
			var capturedColor = preset.color;  // Capture for lambda
			presetButton.Pressed += () => OnColorPresetPressed(capturedColor);
			
			presetsRow.AddChild(presetButton);
		}

		// Add spacing between color and image
		var spacer2 = new Control();
		spacer2.CustomMinimumSize = new Vector2(0, 8);
		backgroundContainer.AddChild(spacer2);

		// Background Image
		var imageRow = new VBoxContainer();
		backgroundContainer.AddChild(imageRow);
		
		var imageHeaderRow = new HBoxContainer();
		imageRow.AddChild(imageHeaderRow);
		
		var imageLabel = new Label();
		imageLabel.Text = "Image:";
		imageLabel.CustomMinimumSize = new Vector2(60, 0);
		imageHeaderRow.AddChild(imageLabel);
		
		_backgroundImageButton = new Button();
		_backgroundImageButton.Name = "BackgroundImageButton";
		_backgroundImageButton.Text = "Select Image...";
		_backgroundImageButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_backgroundImageButton.Pressed += OnBackgroundImageButtonPressed;
		imageHeaderRow.AddChild(_backgroundImageButton);

		// Label to display current image path
		_backgroundImageLabel = new Label();
		_backgroundImageLabel.Name = "BackgroundImageLabel";
		_backgroundImageLabel.Text = "No image selected";
		_backgroundImageLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		_backgroundImageLabel.AddThemeFontSizeOverride("font_size", 10);
		_backgroundImageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		imageRow.AddChild(_backgroundImageLabel);

		// Add spacing
		var spacer3 = new Control();
		spacer3.CustomMinimumSize = new Vector2(0, 4);
		imageRow.AddChild(spacer3);

		// Stretch to Fit checkbox
		var stretchRow = new HBoxContainer();
		imageRow.AddChild(stretchRow);
		
		_stretchToFitCheckbox = new CheckBox();
		_stretchToFitCheckbox.Name = "StretchToFitCheckbox";
		_stretchToFitCheckbox.Text = "Stretch to Fit";
		_stretchToFitCheckbox.ButtonPressed = true;  // Checked by default
		_stretchToFitCheckbox.Toggled += OnStretchToFitToggled;
		stretchRow.AddChild(_stretchToFitCheckbox);

		// Add spacing
		var spacer4 = new Control();
		spacer4.CustomMinimumSize = new Vector2(0, 4);
		imageRow.AddChild(spacer4);

		// Add a "Clear Image" button
		var clearImageButton = new Button();
		clearImageButton.Text = "Clear Image";
		clearImageButton.Pressed += OnClearImageButtonPressed;
		imageRow.AddChild(clearImageButton);
		
		// Add spacing
		var spacer5 = new Control();
		spacer5.CustomMinimumSize = new Vector2(0, 10);
		vbox.AddChild(spacer5);
		
		// Floor section with collapsible dropdown
		_floorSection = new CollapsibleSection("Floor");
		_floorSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_floorSection);
		
		// Hide the reset button for project properties
		_floorSection.GetResetButton().Visible = false;
		
		var floorContainer = _floorSection.GetContentContainer();
		
		// Floor Visibility checkbox
		var visibilityRow = new HBoxContainer();
		floorContainer.AddChild(visibilityRow);
		
		_floorVisibilityCheckbox = new CheckBox();
		_floorVisibilityCheckbox.Name = "FloorVisibilityCheckbox";
		_floorVisibilityCheckbox.Text = "Show Floor";
		_floorVisibilityCheckbox.ButtonPressed = true;  // Visible by default
		_floorVisibilityCheckbox.Toggled += OnFloorVisibilityToggled;
		visibilityRow.AddChild(_floorVisibilityCheckbox);
		
		// Add spacing
		var spacer6 = new Control();
		spacer6.CustomMinimumSize = new Vector2(0, 8);
		floorContainer.AddChild(spacer6);
		
		// Floor Texture selection
		var textureRow = new VBoxContainer();
		floorContainer.AddChild(textureRow);
		
		var textureHeaderRow = new HBoxContainer();
		textureRow.AddChild(textureHeaderRow);
		
		var textureLabel = new Label();
		textureLabel.Text = "Texture:";
		textureLabel.CustomMinimumSize = new Vector2(60, 0);
		textureHeaderRow.AddChild(textureLabel);
		
		_floorTextureDropdown = new OptionButton();
		_floorTextureDropdown.Name = "FloorTextureDropdown";
		_floorTextureDropdown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_floorTextureDropdown.ItemSelected += OnFloorTextureSelected;
		textureHeaderRow.AddChild(_floorTextureDropdown);
		
		// Label to display current texture
		_floorTextureLabel = new Label();
		_floorTextureLabel.Name = "FloorTextureLabel";
		_floorTextureLabel.Text = "Loading textures...";
		_floorTextureLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		_floorTextureLabel.AddThemeFontSizeOverride("font_size", 10);
		_floorTextureLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		textureRow.AddChild(_floorTextureLabel);
	}

	private void FindBackgroundNodes()
	{
		// Navigate to the background nodes in the scene tree
		// Path: Main -> Content -> MainContent -> Viewport -> MainViewport -> SubViewport -> CanvasLayer
		var main = GetTree().Root.GetNode<Control>("Main");
		if (main == null)
		{
			GD.PrintErr("Could not find Main node");
			return;
		}
		
		var subViewport = main.GetNode<SubViewport>("Content/MainContent/Viewport/MainViewport/SubViewport");
		if (subViewport == null)
		{
			GD.PrintErr("Could not find SubViewport");
			return;
		}
		
		var canvasLayer = subViewport.GetNode<CanvasLayer>("CanvasLayer");
		if (canvasLayer == null)
		{
			GD.PrintErr("Could not find CanvasLayer");
			return;
		}
		
		_backgroundColorNode = canvasLayer.GetNode<ColorRect>("BackgroundColor");
		_backgroundImageNode = canvasLayer.GetNode<TextureRect>("BackgroundImage");
		
		if (_backgroundColorNode == null)
		{
			GD.PrintErr("Could not find BackgroundColor node");
		}
		
		if (_backgroundImageNode == null)
		{
			GD.PrintErr("Could not find BackgroundImage node");
		}
	}

	private void LoadCurrentBackgroundSettings()
	{
		// Load the current background color and image from the scene
		if (_backgroundColorNode != null)
		{
			_backgroundColorPicker.Color = _backgroundColorNode.Color;
		}
		
		if (_backgroundImageNode != null)
		{
			// Load texture path if exists
			if (_backgroundImageNode.Texture != null)
			{
				// If there's already a texture, display its path
				if (_backgroundImageNode.Texture is CompressedTexture2D compressedTex)
				{
					_currentBackgroundImagePath = compressedTex.ResourcePath;
					_backgroundImageLabel.Text = _currentBackgroundImagePath;
				}
			}
			
			// Load stretch mode - default to Scale (stretch to fit)
			bool isStretchToFit = _backgroundImageNode.ExpandMode == TextureRect.ExpandModeEnum.IgnoreSize 
				&& _backgroundImageNode.StretchMode == TextureRect.StretchModeEnum.Scale;
			_stretchToFitCheckbox.SetPressedNoSignal(isStretchToFit);
		}
	}

	private void OnBackgroundColorChanged(Color color)
	{
		if (_backgroundColorNode != null)
		{
			_backgroundColorNode.Color = color;
			GD.Print($"Background color changed to: {color}");
		}
	}

	private void OnColorPresetPressed(Color color)
	{
		// Update the color picker visibility
		_backgroundColorPicker.Color = color;
		
		// Manually call the color changed handler to ensure background updates
		OnBackgroundColorChanged(color);
	}

	private void OnBackgroundImageButtonPressed()
	{
		// Use reusable native file dialog
		NativeFileDialog.ShowOpenFile(
			title: "Select Background Image",
			filters: NativeFileDialog.Filters.Images,
			callback: OnImageFileSelected
		);
	}

	private void OnImageFileSelected(bool success, string path)
	{
		// Check if user cancelled or selection failed
		if (!success || string.IsNullOrEmpty(path))
		{
			return;
		}
		
		// Load the selected image
		var image = Image.LoadFromFile(path);
		if (image == null)
		{
			GD.PrintErr($"Failed to load image: {path}");
			return;
		}
		
		var texture = ImageTexture.CreateFromImage(image);
		if (texture == null)
		{
			GD.PrintErr($"Failed to create texture from image: {path}");
			return;
		}
		
		if (_backgroundImageNode != null)
		{
			_backgroundImageNode.Texture = texture;
			_currentBackgroundImagePath = path;
			_backgroundImageLabel.Text = path;
			
			// Apply the current stretch mode setting
			OnStretchToFitToggled(_stretchToFitCheckbox.ButtonPressed);
			
			GD.Print($"Background image changed to: {path}");
		}
	}

	private void OnStretchToFitToggled(bool stretchToFit)
	{
		if (_backgroundImageNode != null)
		{
			if (stretchToFit)
			{
				// Stretch to fit: IgnoreSize + Scale
				_backgroundImageNode.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				_backgroundImageNode.StretchMode = TextureRect.StretchModeEnum.Scale;
				GD.Print("Background image stretch mode: Stretch to Fit");
			}
			else
			{
				// Keep aspect: IgnoreSize + Keep
				_backgroundImageNode.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				_backgroundImageNode.StretchMode = TextureRect.StretchModeEnum.Keep;
				GD.Print("Background image stretch mode: Keep Aspect");
			}
		}
	}

	private void OnClearImageButtonPressed()
	{
		if (_backgroundImageNode != null)
		{
			_backgroundImageNode.Texture = null;
			_currentBackgroundImagePath = "";
			_backgroundImageLabel.Text = "No image selected";
			GD.Print("Background image cleared");
		}
	}
	
	private void FindFloorNode()
	{
		// Navigate to the floor node in the scene tree
		// Path: Main -> Content -> MainContent -> Viewport -> MainViewport -> SubViewport -> Floor
		var main = GetTree().Root.GetNode<Control>("Main");
		if (main == null)
		{
			GD.PrintErr("Could not find Main node");
			return;
		}
		
		var subViewport = main.GetNode<SubViewport>("Content/MainContent/Viewport/MainViewport/SubViewport");
		if (subViewport == null)
		{
			GD.PrintErr("Could not find SubViewport");
			return;
		}
		
		_floorNode = subViewport.GetNode<Node3D>("Floor");
		if (_floorNode == null)
		{
			GD.PrintErr("Could not find Floor node");
			return;
		}
		
		// Get the MeshInstance3D child (first child)
		if (_floorNode.GetChildCount() > 0)
		{
			_floorMeshInstance = _floorNode.GetChild<MeshInstance3D>(0);
			if (_floorMeshInstance == null)
			{
				GD.PrintErr("Could not find MeshInstance3D in Floor node");
			}
		}
		else
		{
			GD.PrintErr("Floor node has no children");
		}
	}
	
	private void LoadBlockTextures()
	{
		var textureLoader = MinecraftTextureLoader.Instance;
		
		if (!textureLoader.IsLoaded)
		{
			_floorTextureLabel.Text = "Textures not loaded";
			_floorTextureDropdown.Disabled = true;
			return;
		}
		
		// Get all block texture paths
		_blockTexturePaths = textureLoader.GetAllBlockTexturePaths().OrderBy(p => p).ToList();
		
		if (_blockTexturePaths.Count == 0)
		{
			_floorTextureLabel.Text = "No block textures found";
			_floorTextureDropdown.Disabled = true;
			return;
		}
		
		// Populate the dropdown
		_floorTextureDropdown.Clear();
		foreach (var texturePath in _blockTexturePaths)
		{
			// Extract just the block name from the path (e.g., "block/stone.png" -> "stone")
			var blockName = texturePath.Substring(6, texturePath.Length - 10); // Remove "block/" and ".png"
			_floorTextureDropdown.AddItem(blockName);
		}
		
		_floorTextureLabel.Text = $"{_blockTexturePaths.Count} block textures available";
		GD.Print($"Loaded {_blockTexturePaths.Count} block textures for floor selection");
	}
	
	private void LoadCurrentFloorSettings()
	{
		// Load the current floor visibility
		if (_floorNode != null)
		{
			_floorVisibilityCheckbox.SetPressedNoSignal(_floorNode.Visible);
		}
		
		// Set default texture to grass_block_top
		if (_blockTexturePaths != null && _blockTexturePaths.Count > 0)
		{
			// Try to find grass_block_top texture
			int grassIndex = _blockTexturePaths.FindIndex(p => p.Contains("grass_block_top"));
			
			if (grassIndex >= 0)
			{
				_floorTextureDropdown.Selected = grassIndex;
				OnFloorTextureSelected(grassIndex);
				GD.Print("Set default floor texture to grass_block_top");
			}
			else
			{
				// Fallback to first texture if grass_block_top not found
				_floorTextureDropdown.Selected = 0;
				OnFloorTextureSelected(0);
				GD.Print("grass_block_top not found, using first available texture");
			}
		}
	}
	
	private void OnFloorVisibilityToggled(bool visible)
	{
		if (_floorNode != null)
		{
			_floorNode.Visible = visible;
			GD.Print($"Floor visibility set to: {visible}");
		}
	}
	
	private void OnFloorTextureSelected(long index)
	{
		if (_floorMeshInstance == null || _blockTexturePaths == null)
		{
			GD.PrintErr("Cannot change floor texture: floor mesh or textures not loaded");
			return;
		}
		
		var selectedPath = _blockTexturePaths[(int)index];
		var textureLoader = MinecraftTextureLoader.Instance;
		var texture = textureLoader.GetTexture(selectedPath);
		
		if (texture == null)
		{
			GD.PrintErr($"Failed to load texture: {selectedPath}");
			return;
		}
		
		// Get or create the material
		StandardMaterial3D material;
		if (_floorMeshInstance.MaterialOverride is StandardMaterial3D existingMaterial)
		{
			material = existingMaterial;
		}
		else
		{
			material = new StandardMaterial3D();
			material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
			material.AlphaScissorThreshold = 0.5f;
			material.AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.Off;
			material.MetallicSpecular = 0.0f;
			material.Uv1Scale = new Vector3(64, 64, 64);
			material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
			_floorMeshInstance.MaterialOverride = material;
		}
		
		// Set the texture
		material.AlbedoTexture = texture;
		
		// Extract block name for display
		var blockName = selectedPath.Substring(6, selectedPath.Length - 10);
		
		// Apply green tint for plant textures
		if (IsPlantTexture(blockName))
		{
			// Minecraft grass green tint: #91BD59 (approximately RGB: 145, 189, 89)
			material.AlbedoColor = new Color(145f / 255f, 189f / 255f, 89f / 255f, 1.0f);
			_floorTextureLabel.Text = $"Selected: {blockName} (with tint)";
			GD.Print($"Floor texture changed to: {selectedPath} with green tint");
		}
		else
		{
			// Reset to white (no tint)
			material.AlbedoColor = new Color(1, 1, 1, 1);
			_floorTextureLabel.Text = $"Selected: {blockName}";
			GD.Print($"Floor texture changed to: {selectedPath}");
		}
	}
	
	private bool IsPlantTexture(string blockName)
	{
		// List of block names that should have green tint applied
		var plantKeywords = new[]
		{
			"grass",
			"leaves",
			"vine",
			"lily_pad",
			"fern",
			"tall_grass",
			"seagrass",
			"kelp",
			"sugar_cane",
			"bamboo",
			"attached_melon_stem",
			"attached_pumpkin_stem",
			"melon_stem",
			"pumpkin_stem"
		};
		
		// Check if the block name contains any of the plant keywords
		foreach (var keyword in plantKeywords)
		{
			if (blockName.Contains(keyword))
			{
				return true;
			}
		}
		
		return false;
	}
}
