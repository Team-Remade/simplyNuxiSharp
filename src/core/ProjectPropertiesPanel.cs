using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace simplyRemadeNuxi.core;

public partial class ProjectPropertiesPanel : Panel
{
	private VBoxContainer _vboxContainer;
	
	// Project settings controls
	private LineEdit _projectNameEdit;
	private SpinBox _resolutionWidthSpinBox;
	private SpinBox _resolutionHeightSpinBox;
	private SpinBox _framerateSpinBox;
	private SpinBox _textureAnimationFpsSpinBox;
	private CollapsibleSection _projectSettingsSection;
	
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
	public Node3D FloorNode => _floorNode;
	
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

		// Project Settings section
		_projectSettingsSection = new CollapsibleSection("Project Settings");
		_projectSettingsSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_projectSettingsSection);
		
		// Hide the reset button for project properties
		_projectSettingsSection.GetResetButton().Visible = false;
		
		var projectSettingsContainer = _projectSettingsSection.GetContentContainer();
		
		// Project Name
		var nameRow = new VBoxContainer();
		projectSettingsContainer.AddChild(nameRow);
		
		var nameLabel = new Label();
		nameLabel.Text = "Project Name:";
		nameRow.AddChild(nameLabel);
		
		_projectNameEdit = new LineEdit();
		_projectNameEdit.Name = "ProjectNameEdit";
		_projectNameEdit.PlaceholderText = "My Animation Project";
		_projectNameEdit.Text = "My Animation Project";
		_projectNameEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_projectNameEdit.TextChanged += OnProjectNameChanged;
		nameRow.AddChild(_projectNameEdit);
		
		// Add spacing
		var spacerName = new Control();
		spacerName.CustomMinimumSize = new Vector2(0, 8);
		projectSettingsContainer.AddChild(spacerName);
		
		// Resolution
		var resolutionRow = new VBoxContainer();
		projectSettingsContainer.AddChild(resolutionRow);

        var resolutionLabel = new Label
        {
            Text = "Resolution:"
        };
        resolutionRow.AddChild(resolutionLabel);
		
		var resolutionInputRow = new HBoxContainer();
		resolutionInputRow.AddThemeConstantOverride("separation", 8);
		resolutionRow.AddChild(resolutionInputRow);

        _resolutionWidthSpinBox = new SpinBox
        {
            Name = "ResolutionWidthSpinBox",
            MinValue = 16,
            MaxValue = 7680,
            Value = 1920,
            Step = 1,
            CustomMinimumSize = new Vector2(100, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _resolutionWidthSpinBox.ValueChanged += OnResolutionChanged;
		resolutionInputRow.AddChild(_resolutionWidthSpinBox);

        var xLabel = new Label
        {
            Text = "×",
            VerticalAlignment = VerticalAlignment.Center
        };
        resolutionInputRow.AddChild(xLabel);

        _resolutionHeightSpinBox = new SpinBox
        {
            Name = "ResolutionHeightSpinBox",
            MinValue = 16,
            MaxValue = 4320,
            Value = 1080,
            Step = 1,
            CustomMinimumSize = new Vector2(100, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _resolutionHeightSpinBox.ValueChanged += OnResolutionChanged;
		resolutionInputRow.AddChild(_resolutionHeightSpinBox);

        // Add preset resolution buttons
        var presetsResolutionLabel = new Label
        {
            Text = "Presets:"
        };
        presetsResolutionLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		presetsResolutionLabel.AddThemeFontSizeOverride("font_size", 11);
		resolutionRow.AddChild(presetsResolutionLabel);
		
		var presetsResolutionRow = new HBoxContainer();
		presetsResolutionRow.AddThemeConstantOverride("separation", 4);
		resolutionRow.AddChild(presetsResolutionRow);
		
		var resolutionPresets = new (string name, int width, int height)[]
		{
			("720p", 1280, 720),
			("1080p", 1920, 1080),
			("1440p", 2560, 1440),
			("4K", 3840, 2160)
		};
		
		foreach (var preset in resolutionPresets)
		{
            var presetButton = new Button
            {
                Text = preset.name,
                CustomMinimumSize = new Vector2(60, 24),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            var capturedWidth = preset.width;
			var capturedHeight = preset.height;
			presetButton.Pressed += () => OnResolutionPresetPressed(capturedWidth, capturedHeight);
			
			presetsResolutionRow.AddChild(presetButton);
		}

        // Add spacing
        var spacerResolution = new Control
        {
            CustomMinimumSize = new Vector2(0, 8)
        };
        projectSettingsContainer.AddChild(spacerResolution);
		
		// Framerate
		var framerateRow = new VBoxContainer();
		projectSettingsContainer.AddChild(framerateRow);

        var framerateLabel = new Label
        {
            Text = "Framerate (FPS):"
        };
        framerateRow.AddChild(framerateLabel);
		
		var framerateInputRow = new HBoxContainer();
		framerateInputRow.AddThemeConstantOverride("separation", 8);
		framerateRow.AddChild(framerateInputRow);

        _framerateSpinBox = new SpinBox
        {
            Name = "FramerateSpinBox",
            MinValue = 1,
            MaxValue = 120,
            Value = 30,
            Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _framerateSpinBox.ValueChanged += OnFramerateChanged;
		framerateInputRow.AddChild(_framerateSpinBox);

        // Add preset framerate buttons
        var presetsFramerateLabel = new Label
        {
            Text = "Presets:"
        };
        presetsFramerateLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		presetsFramerateLabel.AddThemeFontSizeOverride("font_size", 11);
		framerateRow.AddChild(presetsFramerateLabel);
		
		var presetsFramerateRow = new HBoxContainer();
		presetsFramerateRow.AddThemeConstantOverride("separation", 4);
		framerateRow.AddChild(presetsFramerateRow);
		
		var frameratePresets = new int[] { 24, 30, 60, 120 };
		
		foreach (var fps in frameratePresets)
		{
            var presetButton = new Button
            {
                Text = $"{fps}",
                CustomMinimumSize = new Vector2(50, 24),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            var capturedFps = fps;
			presetButton.Pressed += () => OnFrameratePresetPressed(capturedFps);
			
			presetsFramerateRow.AddChild(presetButton);
		}

        // Add spacing
        var spacerFramerate = new Control
        {
            CustomMinimumSize = new Vector2(0, 8)
        };
        projectSettingsContainer.AddChild(spacerFramerate);
		
		// Texture Animation Speed
		var textureAnimationRow = new VBoxContainer();
		projectSettingsContainer.AddChild(textureAnimationRow);

        var textureAnimationLabel = new Label
        {
            Text = "Texture Animation Speed (FPS):"
        };
        textureAnimationRow.AddChild(textureAnimationLabel);
		
		var textureAnimationInputRow = new HBoxContainer();
		textureAnimationInputRow.AddThemeConstantOverride("separation", 8);
		textureAnimationRow.AddChild(textureAnimationInputRow);

        _textureAnimationFpsSpinBox = new SpinBox
        {
            Name = "TextureAnimationFpsSpinBox",
            MinValue = 1,
            MaxValue = 120,
            Value = 20, // Default to Minecraft's 20 ticks per second
            Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Controls the base framerate for animated textures (Minecraft default: 20 fps)"
        };
        _textureAnimationFpsSpinBox.ValueChanged += OnTextureAnimationFpsChanged;
		textureAnimationInputRow.AddChild(_textureAnimationFpsSpinBox);

        // Add preset texture animation fps buttons
        var presetsTextureAnimationLabel = new Label
        {
            Text = "Presets:"
        };
        presetsTextureAnimationLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		presetsTextureAnimationLabel.AddThemeFontSizeOverride("font_size", 11);
		textureAnimationRow.AddChild(presetsTextureAnimationLabel);
		
		var presetsTextureAnimationRow = new HBoxContainer();
		presetsTextureAnimationRow.AddThemeConstantOverride("separation", 4);
		textureAnimationRow.AddChild(presetsTextureAnimationRow);
		
		var textureAnimationPresets = new int[] { 10, 20, 30, 60 };
		
		foreach (var fps in textureAnimationPresets)
		{
            var presetButton = new Button
            {
                Text = $"{fps}",
                CustomMinimumSize = new Vector2(50, 24),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            var capturedFps = fps;
			presetButton.Pressed += () => OnTextureAnimationFpsPresetPressed(capturedFps);
			
			presetsTextureAnimationRow.AddChild(presetButton);
		}

        // Add spacing
        var spacerTextureAnimation = new Control
        {
            CustomMinimumSize = new Vector2(0, 10)
        };
        vbox.AddChild(spacerTextureAnimation);

        // Background section with collapsible dropdown
        _backgroundSection = new CollapsibleSection("Background")
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        vbox.AddChild(_backgroundSection);
		
		// Hide the reset button for project properties
		_backgroundSection.GetResetButton().Visible = false;
		
		var backgroundContainer = _backgroundSection.GetContentContainer();

		// Background Color
		var colorRow = new HBoxContainer();
		backgroundContainer.AddChild(colorRow);

        var colorLabel = new Label
        {
            Text = "Color:",
            CustomMinimumSize = new Vector2(60, 0)
        };
        colorRow.AddChild(colorLabel);

        _backgroundColorPicker = new ColorPickerButton
        {
            Name = "BackgroundColorPicker",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 32),
            EditAlpha = true
        };
        _backgroundColorPicker.ColorChanged += OnBackgroundColorChanged;
		colorRow.AddChild(_backgroundColorPicker);

        // Add color presets row
        var presetsLabel = new Label
        {
            Text = "Presets:",
            CustomMinimumSize = new Vector2(60, 0)
        };
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
            var presetButton = new Button
            {
                CustomMinimumSize = new Vector2(24, 24),
                TooltipText = preset.name
            };

            // Create colored stylebox for the button
            var styleBox = new StyleBoxFlat
            {
                BgColor = preset.color
            };
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
        var spacer2 = new Control
        {
            CustomMinimumSize = new Vector2(0, 8)
        };
        backgroundContainer.AddChild(spacer2);

		// Background Image
		var imageRow = new VBoxContainer();
		backgroundContainer.AddChild(imageRow);
		
		var imageHeaderRow = new HBoxContainer();
		imageRow.AddChild(imageHeaderRow);

        var imageLabel = new Label
        {
            Text = "Image:",
            CustomMinimumSize = new Vector2(60, 0)
        };
        imageHeaderRow.AddChild(imageLabel);

        _backgroundImageButton = new Button
        {
            Name = "BackgroundImageButton",
            Text = "Select Image...",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _backgroundImageButton.Pressed += OnBackgroundImageButtonPressed;
		imageHeaderRow.AddChild(_backgroundImageButton);

        // Label to display current image path
        _backgroundImageLabel = new Label
        {
            Name = "BackgroundImageLabel",
            Text = "No image selected"
        };
        _backgroundImageLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		_backgroundImageLabel.AddThemeFontSizeOverride("font_size", 10);
		_backgroundImageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		imageRow.AddChild(_backgroundImageLabel);

        // Add spacing
        var spacer3 = new Control
        {
            CustomMinimumSize = new Vector2(0, 4)
        };
        imageRow.AddChild(spacer3);

		// Stretch to Fit checkbox
		var stretchRow = new HBoxContainer();
		imageRow.AddChild(stretchRow);

        _stretchToFitCheckbox = new CheckBox
        {
            Name = "StretchToFitCheckbox",
            Text = "Stretch to Fit",
            ButtonPressed = true  // Checked by default
        };
        _stretchToFitCheckbox.Toggled += OnStretchToFitToggled;
		stretchRow.AddChild(_stretchToFitCheckbox);

        // Add spacing
        var spacer4 = new Control
        {
            CustomMinimumSize = new Vector2(0, 4)
        };
        imageRow.AddChild(spacer4);

        // Add a "Clear Image" button
        var clearImageButton = new Button
        {
            Text = "Clear Image"
        };
        clearImageButton.Pressed += OnClearImageButtonPressed;
		imageRow.AddChild(clearImageButton);

        // Add spacing
        var spacer5 = new Control
        {
            CustomMinimumSize = new Vector2(0, 10)
        };
        vbox.AddChild(spacer5);

        // Floor section with collapsible dropdown
        _floorSection = new CollapsibleSection("Floor")
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        vbox.AddChild(_floorSection);
		
		// Hide the reset button for project properties
		_floorSection.GetResetButton().Visible = false;
		
		var floorContainer = _floorSection.GetContentContainer();
		
		// Floor Visibility checkbox
		var visibilityRow = new HBoxContainer();
		floorContainer.AddChild(visibilityRow);

        _floorVisibilityCheckbox = new CheckBox
        {
            Name = "FloorVisibilityCheckbox",
            Text = "Show Floor",
            ButtonPressed = true  // Visible by default
        };
        _floorVisibilityCheckbox.Toggled += OnFloorVisibilityToggled;
		visibilityRow.AddChild(_floorVisibilityCheckbox);

        // Add spacing
        var spacer6 = new Control
        {
            CustomMinimumSize = new Vector2(0, 8)
        };
        floorContainer.AddChild(spacer6);
		
		// Floor Texture selection
		var textureRow = new VBoxContainer();
		floorContainer.AddChild(textureRow);
		
		var textureHeaderRow = new HBoxContainer();
		textureRow.AddChild(textureHeaderRow);

        var textureLabel = new Label
        {
            Text = "Texture:",
            CustomMinimumSize = new Vector2(60, 0)
        };
        textureHeaderRow.AddChild(textureLabel);

        _floorTextureDropdown = new OptionButton
        {
            Name = "FloorTextureDropdown",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _floorTextureDropdown.ItemSelected += OnFloorTextureSelected;
		textureHeaderRow.AddChild(_floorTextureDropdown);

        // Label to display current texture
        _floorTextureLabel = new Label
        {
            Name = "FloorTextureLabel",
            Text = "Loading textures..."
        };
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
			
			// Sync to preview viewport
			Main.Instance?.SyncPreviewBackground();
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
			
			// Sync to preview viewport
			Main.Instance?.SyncPreviewBackground();
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
			}
			else
			{
				// Keep aspect: IgnoreSize + Keep
				_backgroundImageNode.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				_backgroundImageNode.StretchMode = TextureRect.StretchModeEnum.Keep;
			}
			
			// Sync to preview viewport
			Main.Instance?.SyncPreviewBackground();
		}
	}

	private void OnClearImageButtonPressed()
	{
		if (_backgroundImageNode != null)
		{
			_backgroundImageNode.Texture = null;
			_currentBackgroundImagePath = "";
			_backgroundImageLabel.Text = "No image selected";
			
			// Sync to preview viewport
			Main.Instance?.SyncPreviewBackground();
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
			}
			else
			{
				// Fallback to first texture if grass_block_top not found
				_floorTextureDropdown.Selected = 0;
				OnFloorTextureSelected(0);
			}
		}
	}
	
	private void OnFloorVisibilityToggled(bool visible)
	{
		if (_floorNode != null)
		{
			_floorNode.Visible = visible;
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
            material = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                AlphaScissorThreshold = 0.5f,
                AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.Off,
                MetallicSpecular = 0.0f,
                Uv1Scale = new Vector3(64, 64, 64),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest
            };
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
		}
		else
		{
			// Reset to white (no tint)
			material.AlbedoColor = new Color(1, 1, 1, 1);
			_floorTextureLabel.Text = $"Selected: {blockName}";
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
	
	private void OnProjectNameChanged(string newName)
	{
		// TODO: Save to project file or settings
	}
	
	private void OnResolutionChanged(double value)
	{
		var width = (int)_resolutionWidthSpinBox.Value;
		var height = (int)_resolutionHeightSpinBox.Value;
		// TODO: Apply resolution to render viewport
	}
	
	private void OnResolutionPresetPressed(int width, int height)
	{
		_resolutionWidthSpinBox.Value = width;
		_resolutionHeightSpinBox.Value = height;
		// OnResolutionChanged will be called automatically via ValueChanged signal
	}
	
	private void OnFramerateChanged(double value)
	{
		var fps = (int)value;
		// TODO: Apply framerate to animation timeline
	}
	
	private void OnFrameratePresetPressed(int fps)
	{
		_framerateSpinBox.Value = fps;
		// OnFramerateChanged will be called automatically via ValueChanged signal
	}
	
	private void OnTextureAnimationFpsChanged(double value)
	{
		var fps = (float)value;
		
		// Update the AnimatedTextureManager
		if (AnimatedTextureManager.Instance != null)
		{
			AnimatedTextureManager.Instance.SetTextureAnimationFps(fps);
		}
	}
	
	private void OnTextureAnimationFpsPresetPressed(int fps)
	{
		_textureAnimationFpsSpinBox.Value = fps;
		// OnTextureAnimationFpsChanged will be called automatically via ValueChanged signal
	}
	
	/// <summary>
	/// Gets the current render resolution width
	/// </summary>
	public int GetRenderWidth()
	{
		return (int)_resolutionWidthSpinBox.Value;
	}
	
	/// <summary>
	/// Gets the current render resolution height
	/// </summary>
	public int GetRenderHeight()
	{
		return (int)_resolutionHeightSpinBox.Value;
	}
	
	/// <summary>
	/// Gets the current project framerate
	/// </summary>
	public float GetFramerate()
	{
		return (float)_framerateSpinBox.Value;
	}
}
