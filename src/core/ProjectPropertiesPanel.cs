using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using simplyRemadeNuxi.core.commands;

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
	private MeshInstance3D _backgroundColorMesh;
	private MeshInstance3D _backgroundImageMesh;
	
	// Sky controls
	private CheckBox _useSkyCheckbox;
	private CheckBox _useAdvancedSkyCheckbox;
	private WorldEnvironment _worldEnvironment;
	private Godot.Environment _skyEnvironment;
	private ShaderMaterial _skyShaderMaterial;
	
	// Advanced Sky
	private Node3D _advancedSkyNode;
	private MeshInstance3D _minecraftCloudsNode;

	// Sky color controls (Minecraft Sky shader)
	private CollapsibleSection _skySettingsSection;
	private ColorPickerButton _skyHorizonDayColorPicker;
	private ColorPickerButton _skyZenithDayColorPicker;
	private ColorPickerButton _skyHorizonSunsetColorPicker;
	private ColorPickerButton _skyZenithSunsetColorPicker;
	private ColorPickerButton _skyNightHorizonColorPicker;
	private ColorPickerButton _skyNightZenithColorPicker;
	private ColorPickerButton _skyStarsColorPicker;

	// Clouds color control (Minecraft Clouds shader)
	private ColorPickerButton _cloudsColorPicker;

	// Sun rotation controls
	private SpinBox _sunRotationXSpinBox;
	private SpinBox _sunRotationYSpinBox;
	private SpinBox _sunRotationZSpinBox;
	private DirectionalLight3D _sunNode;

	// Pre-edit values for sky color pickers undo/redo
	private Color _preEditSkyHorizonDayColor;
	private Color _preEditSkyZenithDayColor;
	private Color _preEditSkyHorizonSunsetColor;
	private Color _preEditSkyZenithSunsetColor;
	private Color _preEditSkyNightHorizonColor;
	private Color _preEditSkyNightZenithColor;
	private Color _preEditSkyStarsColor;
	private Color _preEditCloudsColor;

	// Pre-edit values for sun rotation spinboxes undo/redo
	private double _preEditSunRotationX;
	private double _preEditSunRotationY;
	private double _preEditSunRotationZ;

	// Advanced Sky atmosphere controls
	private SpinBox _advSkyRayleighSpinBox;
	private SpinBox _advSkyMieSpinBox;
	private SpinBox _advSkyOzoneSpinBox;
	private SpinBox _advSkyAtmDensitySpinBox;
	private SpinBox _advSkyExposureSpinBox;
	private SpinBox _advSkySunDiscFeatherSpinBox;
	private SpinBox _advSkySunDiscIntensitySpinBox;
	private SpinBox _advSkyStarsExposureSpinBox;
	private VBoxContainer _advSkySettingsContainer;

	// Pre-edit values for Advanced Sky spinboxes undo/redo
	private double _preEditAdvSkyRayleigh;
	private double _preEditAdvSkyMie;
	private double _preEditAdvSkyOzone;
	private double _preEditAdvSkyAtmDensity;
	private double _preEditAdvSkyExposure;
	private double _preEditAdvSkySunDiscFeather;
	private double _preEditAdvSkySunDiscIntensity;
	private double _preEditAdvSkyStarsExposure;
	
	// Background color UI containers (disabled when sky is active)
	private HBoxContainer _colorRow;
	private HBoxContainer _colorPresetsRow;
	private Label _colorPresetsLabel;
	
	// Reference to the floor node
	private Node3D _floorNode;
	private MeshInstance3D _floorMeshInstance;
	public Node3D FloorNode => _floorNode;
	public MeshInstance3D BackgroundColorNode => _backgroundColorMesh;
	public MeshInstance3D BackgroundImageNode => _backgroundImageMesh;

	// Public properties for saving/loading background settings
	public Color BackgroundColor => _backgroundColorPicker?.Color ?? new Color("#939BFF");
	public string BackgroundImagePath => _currentBackgroundImagePath;
	public bool StretchBackground => _stretchToFitCheckbox?.ButtonPressed ?? true;
	public bool UseSky => _useSkyCheckbox?.ButtonPressed ?? true;
	public bool UseAdvancedSky => _useAdvancedSkyCheckbox?.ButtonPressed ?? false;

	// Public property for texture animation FPS
	public float TextureAnimationFps => _textureAnimationFpsSpinBox != null ? (float)_textureAnimationFpsSpinBox.Value : 20f;

	// Store the current background image path
	private string _currentBackgroundImagePath = "";
	
	// Store available block textures
	private List<string> _blockTexturePaths;

	// Pre-edit values for spinbox undo/redo (captured on focus-enter)
	private double _preEditResolutionWidth;
	private double _preEditResolutionHeight;
	private double _preEditFramerate;
	private double _preEditTextureAnimFps;

	// Pre-edit color for background color picker undo/redo
	private Color _preEditBackgroundColor;

	public override void _Ready()
	{
		SetupUi();
		FindBackgroundNodes();
		FindFloorNode();
		FindSunNode();

		// Subscribe to project events to reload settings when project is opened
		ProjectManager.ProjectOpened += OnProjectOpened;
		ProjectManager.ProjectClosed += OnProjectClosed;

		// Asset-dependent initialization is deferred until OnAssetsLoaded() is called
		// by Main after the AssetDownloaderWindow has finished loading.
	}

	/// <summary>
	/// Called when a project is opened. Reloads project settings.
	/// </summary>
	private void OnProjectOpened(string projectFolder)
	{
		// Load project name into the edit box (disconnect signal temporarily to avoid triggering OnProjectNameChanged)
		_projectNameEdit.TextChanged -= OnProjectNameChanged;
		_projectNameEdit.Text = ProjectManager.CurrentProjectName;
		_projectNameEdit.TextChanged += OnProjectNameChanged;

		// Reload project settings (resolution and framerate) from the saved project data
		LoadCurrentProjectSettings();
		
		// Reload floor and background settings from the saved project data
		LoadCurrentFloorSettings();
		LoadCurrentBackgroundSettings();

		// Restore WorkCamera position and rotation
		RestoreWorkCameraState();
	}

	/// <summary>
	/// Loads resolution and framerate settings from the saved project data.
	/// </summary>
	private void LoadCurrentProjectSettings()
	{
		var settings = ProjectManager.GetSettings();
		bool hasSavedSettings = settings != null;

		// Load resolution width
		if (hasSavedSettings && settings.RenderWidth > 0)
		{
			_resolutionWidthSpinBox.SetValueNoSignal(settings.RenderWidth);
		}

		// Load resolution height
		if (hasSavedSettings && settings.RenderHeight > 0)
		{
			_resolutionHeightSpinBox.SetValueNoSignal(settings.RenderHeight);
		}

		// Load framerate
		if (hasSavedSettings && settings.Framerate > 0)
		{
			_framerateSpinBox.SetValueNoSignal(settings.Framerate);
		}

		// Load texture animation FPS
		if (hasSavedSettings && settings.TextureAnimationFps > 0)
		{
			_textureAnimationFpsSpinBox.SetValueNoSignal(settings.TextureAnimationFps);
			// Also apply to the animated texture manager
			if (AnimatedTextureManager.Instance != null)
				AnimatedTextureManager.Instance.SetTextureAnimationFps(settings.TextureAnimationFps);
		}
	}

	/// <summary>
	/// Called when a project is closed. Resets to default settings.
	/// </summary>
	private void OnProjectClosed()
	{
		// Reset to default settings when project is closed
		if (_floorNode != null)
		{
			_floorVisibilityCheckbox.SetPressedNoSignal(true);
			_floorNode.Visible = true;
		}

		// Reset background to defaults
		if (_backgroundColorMesh?.MaterialOverride is ShaderMaterial colorShaderMat)
		{
			var defaultColor = new Color("#939BFF");
			colorShaderMat.SetShaderParameter("abledo_color", defaultColor);
			_backgroundColorPicker.Color = defaultColor;
		}
		
		// Reset sky to enabled (default)
		_useSkyCheckbox?.SetPressedNoSignal(true);
		ApplySkySetting(true);
		
		// Reset advanced sky to disabled (default)
		_useAdvancedSkyCheckbox?.SetPressedNoSignal(false);
		ApplyAdvancedSkySetting(false);

		// Reset sky colors and sun rotation to defaults
		ResetSkySettings();
	}

	/// <summary>
	/// Called by Main after all Minecraft assets have been loaded.
	/// Populates any UI that depends on textures / JSON data.
	/// </summary>
	public void OnAssetsLoaded()
	{
		LoadBlockTextures();
		LoadCurrentFloorSettings();
		
		// Apply default sky setting (enabled by default)
		if (_useSkyCheckbox != null)
		{
			_applySkyOnLoad = true;
		}
	}
	
	// Flag to apply sky after background nodes are found
	private bool _applySkyOnLoad = false;

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
  HookSpinBoxUndo(_resolutionWidthSpinBox,
   () => _preEditResolutionWidth, v => _preEditResolutionWidth = v,
   "Change Resolution Width",
   v => _resolutionWidthSpinBox.SetValueNoSignal(v));

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
  HookSpinBoxUndo(_resolutionHeightSpinBox,
   () => _preEditResolutionHeight, v => _preEditResolutionHeight = v,
   "Change Resolution Height",
   v => _resolutionHeightSpinBox.SetValueNoSignal(v));

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
  HookSpinBoxUndo(_framerateSpinBox,
   () => _preEditFramerate, v => _preEditFramerate = v,
   "Change Framerate",
   v => _framerateSpinBox.SetValueNoSignal(v));

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
		HookSpinBoxUndo(_textureAnimationFpsSpinBox,
			() => _preEditTextureAnimFps, v => _preEditTextureAnimFps = v,
			"Change Texture Animation FPS",
			v =>
			{
				_textureAnimationFpsSpinBox.SetValueNoSignal(v);
				if (AnimatedTextureManager.Instance != null)
					AnimatedTextureManager.Instance.SetTextureAnimationFps((float)v);
			});

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

		// Sky checkbox
		var skyRow = new HBoxContainer();
		backgroundContainer.AddChild(skyRow);
		
		var skyLabel = new Label
		{
			Text = "Sky:",
			CustomMinimumSize = new Vector2(60, 0)
		};
		skyRow.AddChild(skyLabel);

		_useSkyCheckbox = new CheckBox
		{
			Name = "UseSkyCheckbox",
			Text = "Use Sky",
			ButtonPressed = true  // Enabled by default
		};
		_useSkyCheckbox.Toggled += OnUseSkyToggled;
		skyRow.AddChild(_useSkyCheckbox);

		// Advanced Sky checkbox row
		var advancedSkyRow = new HBoxContainer();
		backgroundContainer.AddChild(advancedSkyRow);

		var advancedSkyLabel = new Label
		{
			Text = "",
			CustomMinimumSize = new Vector2(60, 0)
		};
		advancedSkyRow.AddChild(advancedSkyLabel);

		_useAdvancedSkyCheckbox = new CheckBox
		{
			Name = "UseAdvancedSkyCheckbox",
			Text = "Use Advanced Sky(Warning: Lag spike on enable)",
			ButtonPressed = false,
			TooltipText = "Replaces the Minecraft sky with a volumetric advanced sky (SunshineClouds2). Hides Minecraft clouds."
		};
		_useAdvancedSkyCheckbox.Toggled += OnUseAdvancedSkyToggled;
		advancedSkyRow.AddChild(_useAdvancedSkyCheckbox);

		// Add spacing
		var skySpacer = new Control
		{
			CustomMinimumSize = new Vector2(0, 8)
		};
		backgroundContainer.AddChild(skySpacer);

		// Background Color
		var colorRow = new HBoxContainer();
		_colorRow = colorRow;
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
  // Capture pre-edit color when the popup opens; record undo when it closes
  _backgroundColorPicker.Ready += () =>
  {
   _backgroundColorPicker.GetPopup().AboutToPopup += () =>
    _preEditBackgroundColor = _backgroundColorPicker.Color;
   _backgroundColorPicker.GetPopup().PopupHide += () =>
   {
    var pre = _preEditBackgroundColor;
    var cur = _backgroundColorPicker.Color;
    if (pre != cur && EditorCommandHistory.Instance != null)
    {
     EditorCommandHistory.Instance.PushWithoutExecute(
      new PropertyChangeCommand<Color>(
       "Change Background Color", pre, cur,
       v =>
       {
        _backgroundColorPicker.Color = v;
        OnBackgroundColorChanged(v);
       }));
    }
   };
  };
  colorRow.AddChild(_backgroundColorPicker);

        // Add color presets row
        var presetsLabel = new Label
        {
            Text = "Presets:",
            CustomMinimumSize = new Vector2(60, 0)
        };
        presetsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
  presetsLabel.AddThemeFontSizeOverride("font_size", 11);
  _colorPresetsLabel = presetsLabel;
  backgroundContainer.AddChild(presetsLabel);
  
  var presetsRow = new HBoxContainer();
  presetsRow.AddThemeConstantOverride("separation", 4);
  _colorPresetsRow = presetsRow;
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

		// Add spacing
		var spacerSkySettings = new Control
		{
			CustomMinimumSize = new Vector2(0, 10)
		};
		vbox.AddChild(spacerSkySettings);

		// Sky Settings section
		_skySettingsSection = new CollapsibleSection("Sky Settings")
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		vbox.AddChild(_skySettingsSection);
		_skySettingsSection.GetResetButton().Visible = false;

		var skySettingsContainer = _skySettingsSection.GetContentContainer();

		// ── Minecraft Sky Colors ──────────────────────────────────────────────
		var skyColorsLabel = new Label { Text = "Minecraft Sky Colors:" };
		skyColorsLabel.AddThemeFontSizeOverride("font_size", 12);
		skySettingsContainer.AddChild(skyColorsLabel);

		var skyColorsSpacer = new Control { CustomMinimumSize = new Vector2(0, 4) };
		skySettingsContainer.AddChild(skyColorsSpacer);

		_skyHorizonDayColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Horizon Day",
			new Color(0.576f, 0.608f, 1.0f, 1.0f),
			c => ApplySkyShaderColor("horizon_day_color", c),
			() => _preEditSkyHorizonDayColor, v => _preEditSkyHorizonDayColor = v);

		_skyZenithDayColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Zenith Day",
			new Color(0.12f, 0.25f, 0.55f, 1.0f),
			c => ApplySkyShaderColor("zenith_day_color", c),
			() => _preEditSkyZenithDayColor, v => _preEditSkyZenithDayColor = v);

		_skyHorizonSunsetColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Horizon Sunset",
			new Color(1.0f, 0.45f, 0.2f, 1.0f),
			c => ApplySkyShaderColor("horizon_sunset_color", c),
			() => _preEditSkyHorizonSunsetColor, v => _preEditSkyHorizonSunsetColor = v);

		_skyZenithSunsetColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Zenith Sunset",
			new Color(0.3f, 0.1f, 0.35f, 1.0f),
			c => ApplySkyShaderColor("zenith_sunset_color", c),
			() => _preEditSkyZenithSunsetColor, v => _preEditSkyZenithSunsetColor = v);

		_skyNightHorizonColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Night Horizon",
			new Color(0.05f, 0.05f, 0.15f, 1.0f),
			c => ApplySkyShaderColor("night_horizon_color", c),
			() => _preEditSkyNightHorizonColor, v => _preEditSkyNightHorizonColor = v);

		_skyNightZenithColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Night Zenith",
			new Color(0.01f, 0.01f, 0.03f, 1.0f),
			c => ApplySkyShaderColor("night_zenith_color", c),
			() => _preEditSkyNightZenithColor, v => _preEditSkyNightZenithColor = v);

		_skyStarsColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Stars",
			new Color(1.0f, 1.0f, 1.0f, 1.0f),
			c => ApplySkyShaderColor("stars_color", c),
			() => _preEditSkyStarsColor, v => _preEditSkyStarsColor = v);

		// ── Minecraft Clouds Color ────────────────────────────────────────────
		var cloudsSpacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
		skySettingsContainer.AddChild(cloudsSpacer);

		var cloudsColorsLabel = new Label { Text = "Minecraft Clouds Color:" };
		cloudsColorsLabel.AddThemeFontSizeOverride("font_size", 12);
		skySettingsContainer.AddChild(cloudsColorsLabel);

		var cloudsSpacer2 = new Control { CustomMinimumSize = new Vector2(0, 4) };
		skySettingsContainer.AddChild(cloudsSpacer2);

		_cloudsColorPicker = CreateSkyColorPickerRow(skySettingsContainer, "Cloud Color",
			new Color(1.0f, 1.0f, 1.0f, 1.0f),
			c => ApplyCloudsShaderColor(c),
			() => _preEditCloudsColor, v => _preEditCloudsColor = v);

		// ── Sun Rotation ──────────────────────────────────────────────────────
		var sunRotSpacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
		skySettingsContainer.AddChild(sunRotSpacer);

		var sunRotLabel = new Label { Text = "Sun Rotation (degrees):" };
		sunRotLabel.AddThemeFontSizeOverride("font_size", 12);
		skySettingsContainer.AddChild(sunRotLabel);

		var sunRotSpacer2 = new Control { CustomMinimumSize = new Vector2(0, 4) };
		skySettingsContainer.AddChild(sunRotSpacer2);

		// X rotation row
		var sunRotXRow = new HBoxContainer();
		sunRotXRow.AddThemeConstantOverride("separation", 8);
		skySettingsContainer.AddChild(sunRotXRow);

		var sunRotXLabel = new Label
		{
			Text = "X (Pitch):",
			CustomMinimumSize = new Vector2(130, 0),
			VerticalAlignment = VerticalAlignment.Center
		};
		sunRotXRow.AddChild(sunRotXLabel);

		_sunRotationXSpinBox = new SpinBox
		{
			Name = "SunRotationXSpinBox",
			MinValue = -180.0,
			MaxValue = 180.0,
			Value = 0.0,
			Step = 0.1,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = "Sun pitch rotation in degrees"
		};
		_sunRotationXSpinBox.ValueChanged += v => ApplySunRotation();
		sunRotXRow.AddChild(_sunRotationXSpinBox);
		HookSpinBoxUndo(_sunRotationXSpinBox,
			() => _preEditSunRotationX, v => _preEditSunRotationX = v,
			"Change Sun Rotation X",
			v => { _sunRotationXSpinBox.SetValueNoSignal(v); ApplySunRotation(); });

		// Y rotation row
		var sunRotYRow = new HBoxContainer();
		sunRotYRow.AddThemeConstantOverride("separation", 8);
		skySettingsContainer.AddChild(sunRotYRow);

		var sunRotYLabel = new Label
		{
			Text = "Y (Yaw):",
			CustomMinimumSize = new Vector2(130, 0),
			VerticalAlignment = VerticalAlignment.Center
		};
		sunRotYRow.AddChild(sunRotYLabel);

		_sunRotationYSpinBox = new SpinBox
		{
			Name = "SunRotationYSpinBox",
			MinValue = -180.0,
			MaxValue = 180.0,
			Value = 0.0,
			Step = 0.1,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = "Sun yaw rotation in degrees"
		};
		_sunRotationYSpinBox.ValueChanged += v => ApplySunRotation();
		sunRotYRow.AddChild(_sunRotationYSpinBox);
		HookSpinBoxUndo(_sunRotationYSpinBox,
			() => _preEditSunRotationY, v => _preEditSunRotationY = v,
			"Change Sun Rotation Y",
			v => { _sunRotationYSpinBox.SetValueNoSignal(v); ApplySunRotation(); });

		// Z rotation row
		var sunRotZRow = new HBoxContainer();
		sunRotZRow.AddThemeConstantOverride("separation", 8);
		skySettingsContainer.AddChild(sunRotZRow);

		var sunRotZLabel = new Label
		{
			Text = "Z (Roll):",
			CustomMinimumSize = new Vector2(130, 0),
			VerticalAlignment = VerticalAlignment.Center
		};
		sunRotZRow.AddChild(sunRotZLabel);

		_sunRotationZSpinBox = new SpinBox
		{
			Name = "SunRotationZSpinBox",
			MinValue = -180.0,
			MaxValue = 180.0,
			Value = 0.0,
			Step = 0.1,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = "Sun roll rotation in degrees"
		};
		_sunRotationZSpinBox.ValueChanged += v => ApplySunRotation();
		sunRotZRow.AddChild(_sunRotationZSpinBox);
		HookSpinBoxUndo(_sunRotationZSpinBox,
			() => _preEditSunRotationZ, v => _preEditSunRotationZ = v,
			"Change Sun Rotation Z",
			v => { _sunRotationZSpinBox.SetValueNoSignal(v); ApplySunRotation(); });

		// Reset sun rotation button
		var sunRotResetSpacer = new Control { CustomMinimumSize = new Vector2(0, 4) };
		skySettingsContainer.AddChild(sunRotResetSpacer);

		var sunRotResetButton = new Button
		{
			Text = "Reset Sun Rotation",
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		sunRotResetButton.Pressed += () =>
		{
			_sunRotationXSpinBox.Value = 0.0;
			_sunRotationYSpinBox.Value = 0.0;
			_sunRotationZSpinBox.Value = 0.0;
			ApplySunRotation();
		};
		skySettingsContainer.AddChild(sunRotResetButton);

		// ── Advanced Sky Settings ─────────────────────────────────────────────
		var advSkySpacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
		skySettingsContainer.AddChild(advSkySpacer);

		var advSkyLabel = new Label { Text = "Advanced Sky Settings:" };
		advSkyLabel.AddThemeFontSizeOverride("font_size", 12);
		skySettingsContainer.AddChild(advSkyLabel);

		var advSkyNote = new Label { Text = "(Only active when 'Use Advanced Sky' is enabled)" };
		advSkyNote.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		advSkyNote.AddThemeFontSizeOverride("font_size", 10);
		advSkyNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		skySettingsContainer.AddChild(advSkyNote);

		var advSkySpacer2 = new Control { CustomMinimumSize = new Vector2(0, 4) };
		skySettingsContainer.AddChild(advSkySpacer2);

		// Store the container so we can show/hide it based on advanced sky state
		_advSkySettingsContainer = skySettingsContainer;

		// Helper to create a labeled spinbox row for Advanced Sky
		SpinBox AddAdvSkySpinRow(string labelText, double minVal, double maxVal, double defaultVal, double step,
			string tooltip, ref double preEditField, string undoDesc, Action<double> applyFn)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			skySettingsContainer.AddChild(row);

			var lbl = new Label
			{
				Text = labelText,
				CustomMinimumSize = new Vector2(130, 0),
				VerticalAlignment = VerticalAlignment.Center
			};
			row.AddChild(lbl);

			var spin = new SpinBox
			{
				MinValue = minVal,
				MaxValue = maxVal,
				Value = defaultVal,
				Step = step,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				TooltipText = tooltip
			};
			spin.ValueChanged += v => applyFn(v);
			row.AddChild(spin);
			// Capture ref via closure workaround
			var preRef = preEditField;
			HookSpinBoxUndo(spin,
				() => preRef, v => preRef = v,
				undoDesc,
				v => { spin.SetValueNoSignal(v); applyFn(v); });
			return spin;
		}

		double dummyRayleigh = 1.0, dummyMie = 1.0, dummyOzone = 1.0, dummyDensity = 1.0;
		double dummyExposure = 10.0, dummyFeather = 0.5, dummyIntensity = 100.0, dummyStars = 5.0;

		_advSkyRayleighSpinBox = AddAdvSkySpinRow("Rayleigh Strength", 0.0, 5.0, 1.0, 0.01,
			"Rayleigh scattering strength (blue sky effect)", ref dummyRayleigh,
			"Change Adv Sky Rayleigh", v => ApplyAdvSkyParam("rayleigh_strength", (float)v));

		_advSkyMieSpinBox = AddAdvSkySpinRow("Mie Strength", 0.0, 5.0, 1.0, 0.01,
			"Mie scattering strength (haze/sun glow)", ref dummyMie,
			"Change Adv Sky Mie", v => ApplyAdvSkyParam("mie_strength", (float)v));

		_advSkyOzoneSpinBox = AddAdvSkySpinRow("Ozone Strength", 0.0, 5.0, 1.0, 0.01,
			"Ozone absorption strength (sky color tint)", ref dummyOzone,
			"Change Adv Sky Ozone", v => ApplyAdvSkyParam("ozone_strength", (float)v));

		_advSkyAtmDensitySpinBox = AddAdvSkySpinRow("Atm. Density", 0.0, 5.0, 1.0, 0.01,
			"Overall atmosphere density", ref dummyDensity,
			"Change Adv Sky Density", v => ApplyAdvSkyParam("atmosphere_density", (float)v));

		_advSkyExposureSpinBox = AddAdvSkySpinRow("Exposure", 0.0, 50.0, 10.0, 0.1,
			"Sky exposure (brightness)", ref dummyExposure,
			"Change Adv Sky Exposure", v => ApplyAdvSkyParam("exposure", (float)v));

		_advSkySunDiscFeatherSpinBox = AddAdvSkySpinRow("Sun Disc Feather", 0.0, 1.0, 0.5, 0.01,
			"Sun disc edge feathering", ref dummyFeather,
			"Change Adv Sky Sun Disc Feather", v => ApplyAdvSkyParam("sun_disc_feather", (float)v));

		_advSkySunDiscIntensitySpinBox = AddAdvSkySpinRow("Sun Disc Intensity", 0.0, 500.0, 100.0, 1.0,
			"Sun disc brightness intensity", ref dummyIntensity,
			"Change Adv Sky Sun Disc Intensity", v => ApplyAdvSkyParam("sundisc_intensity", (float)v));

		_advSkyStarsExposureSpinBox = AddAdvSkySpinRow("Stars Exposure", 0.0, 10.0, 5.0, 0.1,
			"Stars brightness exposure", ref dummyStars,
			"Change Adv Sky Stars Exposure", v => ApplyAdvSkyParam("stars_exposure", (float)v));

		// Reset Advanced Sky button
		var advSkyResetSpacer = new Control { CustomMinimumSize = new Vector2(0, 4) };
		skySettingsContainer.AddChild(advSkyResetSpacer);

		var advSkyResetButton = new Button
		{
			Text = "Reset Advanced Sky Settings",
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		advSkyResetButton.Pressed += () => ResetAdvancedSkySettings();
		skySettingsContainer.AddChild(advSkyResetButton);
	}

	private void FindBackgroundNodes()
	{
		// Navigate to the background nodes in the scene tree
		// Path: Main -> Content -> MainContent -> Viewport -> MainViewport -> SubViewport
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
		
		_backgroundColorMesh = subViewport.GetNodeOrNull<MeshInstance3D>("BackgroundColor");
		_backgroundImageMesh = subViewport.GetNodeOrNull<MeshInstance3D>("BackgroundImage");
		
		// Find the WorldEnvironment node
		_worldEnvironment = subViewport.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		
		// Find the MinecraftClouds node
		_minecraftCloudsNode = subViewport.GetNodeOrNull<MeshInstance3D>("MinecraftClouds");
		
		if (_backgroundColorMesh == null)
		{
			GD.PrintErr("Could not find BackgroundColor MeshInstance3D node");
		}
		
		if (_backgroundImageMesh == null)
		{
			GD.PrintErr("Could not find BackgroundImage MeshInstance3D node");
		}

		// Apply sky setting on load if requested
		if (_applySkyOnLoad && _useSkyCheckbox != null)
		{
			_applySkyOnLoad = false;
			// Apply default sky setting (enabled by default)
			ApplySkySetting(_useSkyCheckbox.ButtonPressed);
		}
		else if (_useSkyCheckbox != null)
		{
			// Apply the current sky state to ensure UI and scene are in sync
			ApplySkySetting(_useSkyCheckbox.ButtonPressed);
		}
	}

	private void LoadCurrentBackgroundSettings()
	{
		// Try to load background settings from the saved project data
		var settings = ProjectManager.GetSettings();
		bool hasSavedSettings = settings != null;

		// Load background color
		if (_backgroundColorMesh != null)
		{
			if (hasSavedSettings && !string.IsNullOrEmpty(settings.BackgroundColor))
			{
				// Use saved color
				try
				{
					var color = new Color(settings.BackgroundColor);
					_backgroundColorPicker.Color = color;
					ApplyBackgroundColor(color);
				}
				catch
				{
					// Fallback to reading from scene
					LoadBackgroundColorFromScene();
				}
			}
			else
			{
				// Load from scene (default behavior)
				LoadBackgroundColorFromScene();
			}
		}

		// Load background image and stretch mode
		if (_backgroundImageMesh != null)
		{
			if (hasSavedSettings && !string.IsNullOrEmpty(settings.BackgroundImagePath))
			{
				// Store the relative path and display it
				_currentBackgroundImagePath = settings.BackgroundImagePath;
				_backgroundImageLabel.Text = settings.BackgroundImagePath;

				// Convert relative path to absolute path for loading
				var absolutePath = GetAbsolutePathForLoading(settings.BackgroundImagePath);
				LoadBackgroundImageFromPath(absolutePath);

				// Apply stretch mode
				_stretchToFitCheckbox.SetPressedNoSignal(settings.StretchBackground);
				ApplyStretchMode(settings.StretchBackground);
			}
			else
			{
				// Load from scene (default behavior)
				LoadBackgroundImageAndStretchFromScene();
			}
		}

		// Load sky setting
		if (hasSavedSettings)
		{
			_useSkyCheckbox.SetPressedNoSignal(settings.UseSky);
			ApplySkySetting(settings.UseSky);
			
			// Load advanced sky setting
			_useAdvancedSkyCheckbox?.SetPressedNoSignal(settings.UseAdvancedSky);
			ApplyAdvancedSkySetting(settings.UseAdvancedSky);
		}
		else
		{
			// Default to sky enabled, advanced sky disabled
			_useSkyCheckbox.SetPressedNoSignal(true);
			ApplySkySetting(true);
			_useAdvancedSkyCheckbox?.SetPressedNoSignal(false);
			ApplyAdvancedSkySetting(false);
		}

		// Load sky colors and sun rotation
		LoadCurrentSkySettings();
	}

	/// <summary>
	/// Restores the WorkCamera position and rotation from the saved project settings.
	/// </summary>
	private void RestoreWorkCameraState()
	{
		var settings = ProjectManager.GetSettings();
		if (settings == null) return;

		var viewport = Main.Instance?.Viewport;
		if (viewport == null) return;

		var workCam = viewport.GetNodeOrNull<WorkCamera>("WorkCam");
		if (workCam == null) return;

		if (settings.WorkCameraPosition != null && settings.WorkCameraPosition.Length == 3)
		{
			workCam.GlobalPosition = new Godot.Vector3(
				settings.WorkCameraPosition[0],
				settings.WorkCameraPosition[1],
				settings.WorkCameraPosition[2]);
		}

		if (settings.WorkCameraRotation != null && settings.WorkCameraRotation.Length == 3)
		{
			workCam.GlobalRotation = new Godot.Vector3(
				settings.WorkCameraRotation[0],
				settings.WorkCameraRotation[1],
				settings.WorkCameraRotation[2]);
		}
	}

	private void LoadBackgroundColorFromScene()
	{
		if (_backgroundColorMesh?.MaterialOverride is ShaderMaterial colorShaderMat)
		{
			var color = colorShaderMat.GetShaderParameter("abledo_color").AsColor();
			_backgroundColorPicker.Color = color;
		}
	}

	private void LoadBackgroundImageAndStretchFromScene()
	{
		if (_backgroundImageMesh?.MaterialOverride is ShaderMaterial shaderMat)
		{
			var tex = shaderMat.GetShaderParameter("albedo_tex").As<Texture2D>();
			if (tex != null && tex is CompressedTexture2D compressedTex)
			{
				var resPath = compressedTex.ResourcePath;
				// Treat the default Untitled.png as "no image selected"
				if (resPath != "res://assets/img/Untitled.png")
				{
					_currentBackgroundImagePath = resPath;
					_backgroundImageLabel.Text = resPath;
				}
			}

			// Load stretch mode from shader parameter
			bool isStretchToFit = shaderMat.GetShaderParameter("stretch").AsBool();
			_stretchToFitCheckbox.SetPressedNoSignal(isStretchToFit);
		}
	}

	private void ApplyBackgroundColor(Color color)
	{
		if (_backgroundColorMesh?.MaterialOverride is ShaderMaterial colorShaderMat)
		{
			colorShaderMat.SetShaderParameter("abledo_color", color);
		}
	}

	private void LoadBackgroundImageFromPath(string path)
	{
		if (_backgroundImageMesh?.MaterialOverride is ShaderMaterial shaderMat)
		{
			if (!string.IsNullOrEmpty(path) && Godot.FileAccess.FileExists(path))
			{
				var img = new Image();
				img.Load(path);


				var texture = ImageTexture.CreateFromImage(img);
				if (texture != null)
				{
					shaderMat.SetShaderParameter("albedo_tex", texture);
					return;
				}
			}

			// Clear the image if path doesn't exist or loading failed
			var defaultTex = GD.Load<Texture2D>("res://assets/img/Untitled.png");
			if (defaultTex != null)
			{
				shaderMat.SetShaderParameter("albedo_tex", defaultTex);
			}
		}
	}

	/// <summary>
	/// Converts a relative or absolute path to an absolute path that Godot can load.
	/// If the path is already absolute, returns it as-is.
	/// If it's a relative path (e.g., "assets/images/image.png"), converts to absolute using the project folder.
	/// </summary>
	private string GetAbsolutePathForLoading(string path)
	{
		if (string.IsNullOrEmpty(path))
			return "";

		// Convert backslashes to forward slashes for consistency
		path = path.Replace('\\', '/');

		// If it's already an absolute path, return as-is
		if (System.IO.Path.IsPathRooted(path))
			return path;

		// It's a relative path - combine with project folder
		var projectFolder = ProjectManager.CurrentProjectFolder;
		if (string.IsNullOrEmpty(projectFolder))
			return path;

		// Combine and convert backslashes
		var absolutePath = System.IO.Path.Combine(projectFolder, path);
		return absolutePath.Replace('\\', '/');
	}

	private void ApplyStretchMode(bool stretch)
	{
		if (_backgroundImageMesh?.MaterialOverride is ShaderMaterial shaderMat)
		{
			shaderMat.SetShaderParameter("stretch", stretch);
		}
	}

	private void OnBackgroundColorChanged(Color color)
	{
		if (_backgroundColorMesh != null)
		{
			if (_backgroundColorMesh.MaterialOverride is ShaderMaterial colorShaderMat)
			{
				colorShaderMat.SetShaderParameter("abledo_color", color);
			}
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
		
		if (_backgroundImageMesh != null)
		{
			if (_backgroundImageMesh.MaterialOverride is ShaderMaterial shaderMat)
			{
				shaderMat.SetShaderParameter("albedo_tex", texture);
				_currentBackgroundImagePath = path;
				_backgroundImageLabel.Text = path;
				
				// Apply the current stretch mode setting
				OnStretchToFitToggled(_stretchToFitCheckbox.ButtonPressed);
			}
		}
	}

	private void OnStretchToFitToggled(bool stretchToFit)
	{
		var old = !stretchToFit; // The old value is the opposite of what was just set
		if (_backgroundImageMesh != null)
		{
			if (_backgroundImageMesh.MaterialOverride is ShaderMaterial shaderMat)
			{
				// Retrieve the actual old value from the shader before applying
				old = shaderMat.GetShaderParameter("stretch").AsBool();
				shaderMat.SetShaderParameter("stretch", stretchToFit);
			}
		}

		if (old != stretchToFit && EditorCommandHistory.Instance != null)
		{
			var capturedOld = old; var capturedNew = stretchToFit;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<bool>(
					"Change Stretch To Fit",
					capturedOld, capturedNew,
					v =>
					{
						_stretchToFitCheckbox.SetPressedNoSignal(v);
						if (_backgroundImageMesh?.MaterialOverride is ShaderMaterial sm)
							sm.SetShaderParameter("stretch", v);
					}));
		}
	}

	private void OnUseSkyToggled(bool useSky)
	{
		// Toggle the sky in the environment
		ApplySkySetting(useSky);
	}

	private void ApplySkySetting(bool useSky)
	{
		if (_worldEnvironment == null)
			return;
		
		// Get the current environment
		var currentEnv = _worldEnvironment.Environment;
		if (currentEnv == null)
			return;
		
		if (useSky)
		{
			// Load the Minecraft sky resource
			var sky = GD.Load<Sky>("res://assets/MinecraftSky.tres");
			if (sky != null)
			{
				currentEnv.BackgroundMode = Godot.Environment.BGMode.Sky;
				currentEnv.Sky = sky;
			}
			
			// Hide the background color mesh (sky replaces it)
			if (_backgroundColorMesh != null)
				_backgroundColorMesh.Visible = false;
		}
		else
		{
			// Revert to color background
			currentEnv.BackgroundMode = Godot.Environment.BGMode.Color;
			
			// Show the background color mesh
			if (_backgroundColorMesh != null)
				_backgroundColorMesh.Visible = true;
		}
		
		// Force environment update
		_worldEnvironment.Environment = currentEnv;

		// Re-apply sky colors to the newly loaded sky material
		if (useSky)
			ApplyAllSkyColors();
		
		// Update the background color UI controls (disabled when sky is active)
		UpdateBackgroundColorUiState(useSky);
	}

	/// <summary>
	/// Enables or disables the background color picker and presets based on whether the sky is active.
	/// When the sky is active, the background color node is hidden and the color controls are disabled.
	/// </summary>
	private void UpdateBackgroundColorUiState(bool skyActive)
	{
		bool colorEnabled = !skyActive;
		
		if (_backgroundColorPicker != null)
			_backgroundColorPicker.Disabled = !colorEnabled;
		
		if (_colorRow != null)
			_colorRow.Modulate = colorEnabled ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.4f);
		
		if (_colorPresetsRow != null)
			_colorPresetsRow.Modulate = colorEnabled ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.4f);
		
		if (_colorPresetsLabel != null)
			_colorPresetsLabel.Modulate = colorEnabled ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.4f);
	}

	private void OnUseAdvancedSkyToggled(bool useAdvancedSky)
	{
		ApplyAdvancedSkySetting(useAdvancedSky);
	}

	/// <summary>
	/// Enables or disables the Advanced Sky (SunshineClouds2 volumetric sky).
	/// When enabled: instantiates AdvancedSky.tscn into the SubViewport, sets the WorldEnvironment
	/// sky to AdvancedSky.tres and compositor to AdvancedSkyCompositor.tres, hides MinecraftClouds,
	/// and registers the Sun and Moon directional lights with the CloudDriver.
	/// When disabled: removes the AdvancedSky node, restores the Minecraft sky, and shows MinecraftClouds.
	/// </summary>
	private void ApplyAdvancedSkySetting(bool useAdvancedSky)
	{
		if (_worldEnvironment == null)
			return;

		var subViewport = _worldEnvironment.GetParent() as SubViewport;
		if (subViewport == null)
			return;

		var currentEnv = _worldEnvironment.Environment;
		if (currentEnv == null)
			return;

		if (useAdvancedSky)
		{
			// Instantiate AdvancedSky.tscn if not already present
			if (_advancedSkyNode == null || !IsInstanceValid(_advancedSkyNode))
			{
				var advancedSkyScene = GD.Load<PackedScene>("res://scenes/AdvancedSky.tscn");
				if (advancedSkyScene == null)
				{
					GD.PrintErr("Could not load AdvancedSky.tscn");
					return;
				}
				_advancedSkyNode = advancedSkyScene.Instantiate<Node3D>();
				_advancedSkyNode.Name = "AdvancedSky";
				subViewport.AddChild(_advancedSkyNode);
			}
			else
			{
				_advancedSkyNode.Visible = true;
			}

			// Set the WorldEnvironment sky to AdvancedSky.tres and replace the sky shader
			var advancedSky = GD.Load<Sky>("res://assets/AdvancedSky.tres");
			if (advancedSky != null)
			{
				currentEnv.BackgroundMode = Godot.Environment.BGMode.Sky;
				currentEnv.Sky = advancedSky;
				
				// Explicitly set the sky material shader to AdvancedSky.gdshader
				var advancedSkyShader = GD.Load<Shader>("res://assets/Shaders/AdvancedSky.gdshader");
				if (advancedSkyShader != null && advancedSky.SkyMaterial is ShaderMaterial skyMat)
				{
					skyMat.Shader = advancedSkyShader;
				}
			}

			// Set the compositor to AdvancedSkyCompositor.tres
			var advancedSkyCompositor = GD.Load<Compositor>("res://assets/AdvancedSkyCompositor.tres");
			if (advancedSkyCompositor != null)
			{
				_worldEnvironment.Compositor = advancedSkyCompositor;
			}

			// Hide MinecraftClouds
			if (_minecraftCloudsNode != null)
				_minecraftCloudsNode.Visible = false;

			// Register Sun and Moon directional lights with the CloudDriver
			RegisterLightsWithCloudDriver(subViewport);

			// Force environment update
			_worldEnvironment.Environment = currentEnv;

			// Re-apply Advanced Sky settings to the newly loaded shader material
			ApplyAllAdvancedSkySettings();

			// Re-apply sky colors to the Advanced Sky material
			ApplyAllSkyColors();

			// Temporarily enable render mode to initialize cloud shadows properly
			// This is a workaround for clouds disappearing when shadows are enabled on Sun/Moon
			Main.Instance?.TemporarilyEnableRenderMode();
		}
		else
		{
			// Remove or hide the AdvancedSky node
			if (_advancedSkyNode != null && IsInstanceValid(_advancedSkyNode))
			{
				_advancedSkyNode.QueueFree();
				_advancedSkyNode = null;
			}

			// Remove the compositor
			_worldEnvironment.Compositor = null;

			// Restore the Minecraft sky (if sky is still enabled)
			if (_useSkyCheckbox?.ButtonPressed ?? true)
			{
				var minecraftSky = GD.Load<Sky>("res://assets/MinecraftSky.tres");
				if (minecraftSky != null)
				{
					currentEnv.BackgroundMode = Godot.Environment.BGMode.Sky;
					currentEnv.Sky = minecraftSky;
					
					// Restore the sky material shader to MinecraftSky.gdshader
					var minecraftSkyShader = GD.Load<Shader>("res://assets/Shaders/MinecraftSky.gdshader");
					if (minecraftSkyShader != null && minecraftSky.SkyMaterial is ShaderMaterial skyMat)
					{
						skyMat.Shader = minecraftSkyShader;
					}
				}
			}

			// Show MinecraftClouds
			if (_minecraftCloudsNode != null)
				_minecraftCloudsNode.Visible = true;

			// Force environment update
			_worldEnvironment.Environment = currentEnv;

			// Re-apply sky colors to the restored Minecraft sky material
			ApplyAllSkyColors();
		}

		// Update the advanced sky checkbox state (in case called programmatically)
		_useAdvancedSkyCheckbox?.SetPressedNoSignal(useAdvancedSky);
	}

	/// <summary>
	/// Finds the Sun and Moon DirectionalLight3D nodes in the SubViewport and registers them
	/// with the CloudDriver node inside the AdvancedSky instance.
	/// </summary>
	private void RegisterLightsWithCloudDriver(SubViewport subViewport)
	{
		if (_advancedSkyNode == null || !IsInstanceValid(_advancedSkyNode))
			return;

		// Find the CloudDriver node inside AdvancedSky
		var cloudDriver = _advancedSkyNode.GetNodeOrNull<Node>("CloudDriver");
		if (cloudDriver == null)
		{
			GD.PrintErr("Could not find CloudDriver node in AdvancedSky");
			return;
		}

		// Find Sun and Moon directional lights in the SubViewport
		var sun = subViewport.GetNodeOrNull<DirectionalLight3D>("Sun");
		var moon = subViewport.GetNodeOrNull<DirectionalLight3D>("Sun/Moon");

		// Build the tracked lights array
		var lights = new Godot.Collections.Array<DirectionalLight3D>();
		if (sun != null) lights.Add(sun);
		if (moon != null) lights.Add(moon);

		if (lights.Count > 0)
		{
			cloudDriver.Set("tracked_directional_lights", lights);
		}
		else
		{
			GD.PrintErr("Could not find Sun or Moon directional lights to register with CloudDriver");
		}
	}

	private void OnClearImageButtonPressed()
	{
		if (_backgroundImageMesh != null)
		{
			if (_backgroundImageMesh.MaterialOverride is ShaderMaterial shaderMat)
			{
				// Restore the default Untitled.png texture
				var defaultTex = GD.Load<Texture2D>("res://assets/img/Untitled.png");
				shaderMat.SetShaderParameter("albedo_tex", defaultTex);
				_currentBackgroundImagePath = "";
				_backgroundImageLabel.Text = "No image selected";
			}
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

	private void FindSunNode()
	{
		var main = GetTree().Root.GetNode<Control>("Main");
		if (main == null) return;

		var subViewport = main.GetNodeOrNull<SubViewport>("Content/MainContent/Viewport/MainViewport/SubViewport");
		if (subViewport == null) return;

		_sunNode = subViewport.GetNodeOrNull<DirectionalLight3D>("Sun");
		if (_sunNode == null)
			GD.PrintErr("ProjectPropertiesPanel: Could not find Sun DirectionalLight3D node");
	}

	// ── Sky color picker factory ──────────────────────────────────────────────

	/// <summary>
	/// Creates a labeled color picker row inside <paramref name="container"/> and returns the picker.
	/// </summary>
	private ColorPickerButton CreateSkyColorPickerRow(
		VBoxContainer container,
		string labelText,
		Color defaultColor,
		Action<Color> onChanged,
		Func<Color> getPreEdit,
		Action<Color> setPreEdit)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		container.AddChild(row);

		var lbl = new Label
		{
			Text = labelText,
			CustomMinimumSize = new Vector2(130, 0),
			VerticalAlignment = VerticalAlignment.Center
		};
		row.AddChild(lbl);

		var picker = new ColorPickerButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 28),
			EditAlpha = true,
			Color = defaultColor
		};
		picker.ColorChanged += (Color c) => onChanged(c);
		picker.Ready += () =>
		{
			picker.GetPopup().AboutToPopup += () => setPreEdit(picker.Color);
			picker.GetPopup().PopupHide += () =>
			{
				var pre = getPreEdit();
				var cur = picker.Color;
				if (pre != cur && EditorCommandHistory.Instance != null)
				{
					var capturedPre = pre; var capturedCur = cur;
					var capturedPicker = picker;
					var capturedOnChanged = onChanged;
					EditorCommandHistory.Instance.PushWithoutExecute(
						new PropertyChangeCommand<Color>(
							$"Change {labelText}",
							capturedPre, capturedCur,
							v => { capturedPicker.Color = v; capturedOnChanged(v); }));
				}
			};
		};
		row.AddChild(picker);
		return picker;
	}

	// ── Sky shader color application ──────────────────────────────────────────

	/// <summary>
	/// Gets the ShaderMaterial from the currently active Minecraft sky resource.
	/// Returns null if the sky is not active or the material is not a ShaderMaterial.
	/// </summary>
	private ShaderMaterial GetMinecraftSkyMaterial()
	{
		if (_worldEnvironment?.Environment?.Sky?.SkyMaterial is ShaderMaterial mat)
			return mat;
		return null;
	}

	/// <summary>
	/// Gets the ShaderMaterial from the MinecraftClouds mesh.
	/// Returns null if not found.
	/// </summary>
	private ShaderMaterial GetMinecraftCloudsMaterial()
	{
		if (_minecraftCloudsNode?.MaterialOverride is ShaderMaterial mat)
			return mat;
		return null;
	}

	/// <summary>
	/// Applies a color to a named shader parameter on the Minecraft sky material.
	/// Also applies to the Advanced Sky material if it is currently active.
	/// </summary>
	private void ApplySkyShaderColor(string paramName, Color color)
	{
		var mat = GetMinecraftSkyMaterial();
		mat?.SetShaderParameter(paramName, color);

		// Also apply to Advanced Sky if active
		var advMat = GetAdvancedSkyMaterial();
		advMat?.SetShaderParameter(paramName, color);
	}

	/// <summary>
	/// Applies a color to the cloud_color shader parameter on the Minecraft clouds material.
	/// </summary>
	private void ApplyCloudsShaderColor(Color color)
	{
		var mat = GetMinecraftCloudsMaterial();
		mat?.SetShaderParameter("cloud_color", color);
	}

	/// <summary>
	/// Applies all sky color picker values to the Minecraft sky shader material.
	/// Also applies to the Advanced Sky material if it is currently active.
	/// Called after the sky is loaded/changed to ensure colors are in sync.
	/// </summary>
	private void ApplyAllSkyColors()
	{
		// Apply to Minecraft sky material
		var mat = GetMinecraftSkyMaterial();
		if (mat != null)
		{
			if (_skyHorizonDayColorPicker != null)
				mat.SetShaderParameter("horizon_day_color", _skyHorizonDayColorPicker.Color);
			if (_skyZenithDayColorPicker != null)
				mat.SetShaderParameter("zenith_day_color", _skyZenithDayColorPicker.Color);
			if (_skyHorizonSunsetColorPicker != null)
				mat.SetShaderParameter("horizon_sunset_color", _skyHorizonSunsetColorPicker.Color);
			if (_skyZenithSunsetColorPicker != null)
				mat.SetShaderParameter("zenith_sunset_color", _skyZenithSunsetColorPicker.Color);
			if (_skyNightHorizonColorPicker != null)
				mat.SetShaderParameter("night_horizon_color", _skyNightHorizonColorPicker.Color);
			if (_skyNightZenithColorPicker != null)
				mat.SetShaderParameter("night_zenith_color", _skyNightZenithColorPicker.Color);
			if (_skyStarsColorPicker != null)
				mat.SetShaderParameter("stars_color", _skyStarsColorPicker.Color);
		}

		// Also apply to Advanced Sky material if active
		var advMat = GetAdvancedSkyMaterial();
		if (advMat != null)
		{
			if (_skyHorizonDayColorPicker != null)
				advMat.SetShaderParameter("horizon_day_color", _skyHorizonDayColorPicker.Color);
			if (_skyZenithDayColorPicker != null)
				advMat.SetShaderParameter("zenith_day_color", _skyZenithDayColorPicker.Color);
			if (_skyHorizonSunsetColorPicker != null)
				advMat.SetShaderParameter("horizon_sunset_color", _skyHorizonSunsetColorPicker.Color);
			if (_skyZenithSunsetColorPicker != null)
				advMat.SetShaderParameter("zenith_sunset_color", _skyZenithSunsetColorPicker.Color);
			if (_skyNightHorizonColorPicker != null)
				advMat.SetShaderParameter("night_horizon_color", _skyNightHorizonColorPicker.Color);
			if (_skyNightZenithColorPicker != null)
				advMat.SetShaderParameter("night_zenith_color", _skyNightZenithColorPicker.Color);
			if (_skyStarsColorPicker != null)
				advMat.SetShaderParameter("stars_color", _skyStarsColorPicker.Color);
		}
	}

	/// <summary>
	/// Applies the clouds color picker value to the Minecraft clouds shader material.
	/// </summary>
	private void ApplyAllCloudsColors()
	{
		if (_cloudsColorPicker != null)
			ApplyCloudsShaderColor(_cloudsColorPicker.Color);
	}

	// ── Load sky colors from scene ───────────────────────────────────────────

	/// <summary>
	/// Loads sky and cloud colors from the current shader in the scene.
	/// Returns true if values were successfully loaded from the scene.
	/// </summary>
	private bool LoadSkyColorsFromScene()
	{
		var skyMat = GetMinecraftSkyMaterial();
		var cloudsMat = GetMinecraftCloudsMaterial();

		if (skyMat == null && cloudsMat == null)
			return false;

		// Load sky colors from shader
		if (skyMat != null)
		{
			TrySetColorPickerFromShader(_skyHorizonDayColorPicker,    skyMat, "horizon_day_color");
			TrySetColorPickerFromShader(_skyZenithDayColorPicker,     skyMat, "zenith_day_color");
			TrySetColorPickerFromShader(_skyHorizonSunsetColorPicker, skyMat, "horizon_sunset_color");
			TrySetColorPickerFromShader(_skyZenithSunsetColorPicker,  skyMat, "zenith_sunset_color");
			TrySetColorPickerFromShader(_skyNightHorizonColorPicker,  skyMat, "night_horizon_color");
			TrySetColorPickerFromShader(_skyNightZenithColorPicker,   skyMat, "night_zenith_color");
			TrySetColorPickerFromShader(_skyStarsColorPicker,         skyMat, "stars_color");
		}

		// Load clouds color from shader
		if (cloudsMat != null)
		{
			TrySetColorPickerFromShader(_cloudsColorPicker, cloudsMat, "cloud_color");
		}

		return true;
	}

	/// <summary>
	/// Tries to set a color picker's color from a shader parameter.
	/// </summary>
	private void TrySetColorPickerFromShader(ColorPickerButton picker, ShaderMaterial mat, string paramName)
	{
		if (picker == null) return;
		picker.Color = mat.GetShaderParameter(paramName).AsColor();
	}

	// ── Load sun rotation from scene ─────────────────────────────────────────

	/// <summary>
	/// Loads sun rotation from the Sun node in the scene.
	/// Returns true if values were successfully loaded from the scene.
	/// </summary>
	private bool LoadSunRotationFromScene()
	{
		if (_sunNode == null || _sunRotationXSpinBox == null)
			return false;

		var rotation = _sunNode.RotationDegrees;
		_sunRotationXSpinBox.SetValueNoSignal(rotation.X);
		_sunRotationYSpinBox.SetValueNoSignal(rotation.Y);
		_sunRotationZSpinBox.SetValueNoSignal(rotation.Z);
		return true;
	}

	// ── Sun rotation application ──────────────────────────────────────────────

	/// <summary>
	/// Reads the current spinbox values and applies them as the Sun node's rotation (in degrees).
	/// </summary>
	private void ApplySunRotation()
	{
		if (_sunNode == null) return;
		_sunNode.RotationDegrees = new Vector3(
			(float)_sunRotationXSpinBox.Value,
			(float)_sunRotationYSpinBox.Value,
			(float)_sunRotationZSpinBox.Value);
	}

	// ── Advanced Sky application ──────────────────────────────────────────────

	/// <summary>
	/// Gets the ShaderMaterial from the currently active Advanced Sky resource.
	/// Returns null if the advanced sky is not active or the material is not a ShaderMaterial.
	/// </summary>
	private ShaderMaterial GetAdvancedSkyMaterial()
	{
		if (_worldEnvironment?.Environment?.Sky?.SkyMaterial is ShaderMaterial mat)
		{
			// Only return if it's the advanced sky shader (not the Minecraft sky)
			if (mat.Shader?.ResourcePath?.Contains("AdvancedSky") == true)
				return mat;
		}
		return null;
	}

	/// <summary>
	/// Applies a float parameter to the Advanced Sky shader material.
	/// </summary>
	private void ApplyAdvSkyParam(string paramName, float value)
	{
		var mat = GetAdvancedSkyMaterial();
		mat?.SetShaderParameter(paramName, value);
	}

	/// <summary>
	/// Applies all Advanced Sky spinbox values to the Advanced Sky shader material.
	/// Called after the advanced sky is loaded/changed.
	/// </summary>
	private void ApplyAllAdvancedSkySettings()
	{
		var mat = GetAdvancedSkyMaterial();
		if (mat == null) return;

		if (_advSkyRayleighSpinBox != null)
			mat.SetShaderParameter("rayleigh_strength", (float)_advSkyRayleighSpinBox.Value);
		if (_advSkyMieSpinBox != null)
			mat.SetShaderParameter("mie_strength", (float)_advSkyMieSpinBox.Value);
		if (_advSkyOzoneSpinBox != null)
			mat.SetShaderParameter("ozone_strength", (float)_advSkyOzoneSpinBox.Value);
		if (_advSkyAtmDensitySpinBox != null)
			mat.SetShaderParameter("atmosphere_density", (float)_advSkyAtmDensitySpinBox.Value);
		if (_advSkyExposureSpinBox != null)
			mat.SetShaderParameter("exposure", (float)_advSkyExposureSpinBox.Value);
		if (_advSkySunDiscFeatherSpinBox != null)
			mat.SetShaderParameter("sun_disc_feather", (float)_advSkySunDiscFeatherSpinBox.Value);
		if (_advSkySunDiscIntensitySpinBox != null)
			mat.SetShaderParameter("sundisc_intensity", (float)_advSkySunDiscIntensitySpinBox.Value);
		if (_advSkyStarsExposureSpinBox != null)
			mat.SetShaderParameter("stars_exposure", (float)_advSkyStarsExposureSpinBox.Value);
	}

	/// <summary>
	/// Resets all Advanced Sky spinboxes to their default values.
	/// </summary>
	private void ResetAdvancedSkySettings()
	{
		_advSkyRayleighSpinBox?.SetValueNoSignal(1.0);
		_advSkyMieSpinBox?.SetValueNoSignal(1.0);
		_advSkyOzoneSpinBox?.SetValueNoSignal(1.0);
		_advSkyAtmDensitySpinBox?.SetValueNoSignal(1.0);
		_advSkyExposureSpinBox?.SetValueNoSignal(10.0);
		_advSkySunDiscFeatherSpinBox?.SetValueNoSignal(0.5);
		_advSkySunDiscIntensitySpinBox?.SetValueNoSignal(100.0);
		_advSkyStarsExposureSpinBox?.SetValueNoSignal(5.0);
		ApplyAllAdvancedSkySettings();
	}

	// ── Public accessors for save/load ────────────────────────────────────────

	/// <summary>
	/// Returns the current sky shader color values for saving to the project.
	/// </summary>
	public SkyShaderColors GetSkyShaderColors()
	{
		return new SkyShaderColors
		{
			HorizonDayColor    = _skyHorizonDayColorPicker?.Color.ToHtml(true) ?? "",
			ZenithDayColor     = _skyZenithDayColorPicker?.Color.ToHtml(true) ?? "",
			HorizonSunsetColor = _skyHorizonSunsetColorPicker?.Color.ToHtml(true) ?? "",
			ZenithSunsetColor  = _skyZenithSunsetColorPicker?.Color.ToHtml(true) ?? "",
			NightHorizonColor  = _skyNightHorizonColorPicker?.Color.ToHtml(true) ?? "",
			NightZenithColor   = _skyNightZenithColorPicker?.Color.ToHtml(true) ?? "",
			StarsColor         = _skyStarsColorPicker?.Color.ToHtml(true) ?? "",
		};
	}

	/// <summary>
	/// Returns the current clouds color as an HTML string for saving.
	/// </summary>
	public string GetCloudsColor()
	{
		return _cloudsColorPicker?.Color.ToHtml(true) ?? "";
	}

	/// <summary>
	/// Returns the current sun rotation in degrees, or null if the sun node is not found.
	/// </summary>
	public Vector3? GetSunRotationDegrees()
	{
		if (_sunRotationXSpinBox == null) return null;
		return new Vector3(
			(float)_sunRotationXSpinBox.Value,
			(float)_sunRotationYSpinBox.Value,
			(float)_sunRotationZSpinBox.Value);
	}

	/// <summary>
	/// Returns the current Advanced Sky atmosphere settings for saving to the project.
	/// </summary>
	public AdvancedSkySettings GetAdvancedSkySettings()
	{
		return new AdvancedSkySettings
		{
			RayleighStrength  = _advSkyRayleighSpinBox != null ? (float)_advSkyRayleighSpinBox.Value : float.NaN,
			MieStrength       = _advSkyMieSpinBox != null ? (float)_advSkyMieSpinBox.Value : float.NaN,
			OzoneStrength     = _advSkyOzoneSpinBox != null ? (float)_advSkyOzoneSpinBox.Value : float.NaN,
			AtmDensity        = _advSkyAtmDensitySpinBox != null ? (float)_advSkyAtmDensitySpinBox.Value : float.NaN,
			Exposure          = _advSkyExposureSpinBox != null ? (float)_advSkyExposureSpinBox.Value : float.NaN,
			SunDiscFeather    = _advSkySunDiscFeatherSpinBox != null ? (float)_advSkySunDiscFeatherSpinBox.Value : float.NaN,
			SunDiscIntensity  = _advSkySunDiscIntensitySpinBox != null ? (float)_advSkySunDiscIntensitySpinBox.Value : float.NaN,
			StarsExposure     = _advSkyStarsExposureSpinBox != null ? (float)_advSkyStarsExposureSpinBox.Value : float.NaN,
		};
	}

	// ── Load sky settings from project ────────────────────────────────────────

	/// <summary>
	/// Loads sky color and sun rotation settings. Prioritizes saved project settings
	/// when they exist, then falls back to reading from the scene shader, then to defaults.
	/// </summary>
	private void LoadCurrentSkySettings()
	{
		// Check for saved project settings first
		var settings = ProjectManager.GetSettings();
		bool hasSavedSkyColors = settings != null && !string.IsNullOrEmpty(settings.SkyHorizonDayColor);

		if (hasSavedSkyColors)
		{
			// Load Minecraft sky colors from saved settings
			TrySetColorPicker(_skyHorizonDayColorPicker,    settings.SkyHorizonDayColor,    new Color(0.576f, 0.608f, 1.0f, 1.0f));
			TrySetColorPicker(_skyZenithDayColorPicker,     settings.SkyZenithDayColor,     new Color(0.12f, 0.25f, 0.55f, 1.0f));
			TrySetColorPicker(_skyHorizonSunsetColorPicker, settings.SkyHorizonSunsetColor, new Color(1.0f, 0.45f, 0.2f, 1.0f));
			TrySetColorPicker(_skyZenithSunsetColorPicker,  settings.SkyZenithSunsetColor,  new Color(0.3f, 0.1f, 0.35f, 1.0f));
			TrySetColorPicker(_skyNightHorizonColorPicker,  settings.SkyNightHorizonColor,  new Color(0.05f, 0.05f, 0.15f, 1.0f));
			TrySetColorPicker(_skyNightZenithColorPicker,   settings.SkyNightZenithColor,   new Color(0.01f, 0.01f, 0.03f, 1.0f));
			TrySetColorPicker(_skyStarsColorPicker,         settings.SkyStarsColor,         new Color(1.0f, 1.0f, 1.0f, 1.0f));

			// Apply sky colors to the shader
			ApplyAllSkyColors();

			// Load clouds color
			TrySetColorPicker(_cloudsColorPicker, settings.CloudsColor, new Color(1.0f, 1.0f, 1.0f, 1.0f));
			ApplyAllCloudsColors();

			// Load sun rotation
			if (!float.IsNaN(settings.SunRotationX) && _sunRotationXSpinBox != null)
			{
				_sunRotationXSpinBox.SetValueNoSignal(settings.SunRotationX);
				_sunRotationYSpinBox.SetValueNoSignal(settings.SunRotationY);
				_sunRotationZSpinBox.SetValueNoSignal(settings.SunRotationZ);
				ApplySunRotation();
			}
			else
			{
				// Fall back to reading sun rotation from scene
				LoadSunRotationFromScene();
			}

			// Load Advanced Sky settings
			if (!float.IsNaN(settings.AdvSkyRayleighStrength) && _advSkyRayleighSpinBox != null)
			{
				_advSkyRayleighSpinBox.SetValueNoSignal(settings.AdvSkyRayleighStrength);
				_advSkyMieSpinBox.SetValueNoSignal(settings.AdvSkyMieStrength);
				_advSkyOzoneSpinBox.SetValueNoSignal(settings.AdvSkyOzoneStrength);
				_advSkyAtmDensitySpinBox.SetValueNoSignal(settings.AdvSkyAtmDensity);
				_advSkyExposureSpinBox.SetValueNoSignal(settings.AdvSkyExposure);
				_advSkySunDiscFeatherSpinBox.SetValueNoSignal(settings.AdvSkySunDiscFeather);
				_advSkySunDiscIntensitySpinBox.SetValueNoSignal(settings.AdvSkySunDiscIntensity);
				_advSkyStarsExposureSpinBox.SetValueNoSignal(settings.AdvSkyStarsExposure);
				ApplyAllAdvancedSkySettings();
			}
		}
		else
		{
			// No saved sky settings — fall back to reading from the current scene shader
			bool loadedFromScene = LoadSkyColorsFromScene();
			loadedFromScene = LoadSunRotationFromScene() || loadedFromScene;

			if (loadedFromScene)
			{
				ApplyAllSkyColors();
				ApplyAllCloudsColors();
				return;
			}

			// Last resort: apply defaults
			if (settings != null)
			{
				TrySetColorPicker(_skyHorizonDayColorPicker,    settings.SkyHorizonDayColor,    new Color(0.576f, 0.608f, 1.0f, 1.0f));
				TrySetColorPicker(_skyZenithDayColorPicker,     settings.SkyZenithDayColor,     new Color(0.12f, 0.25f, 0.55f, 1.0f));
				TrySetColorPicker(_skyHorizonSunsetColorPicker, settings.SkyHorizonSunsetColor, new Color(1.0f, 0.45f, 0.2f, 1.0f));
				TrySetColorPicker(_skyZenithSunsetColorPicker,  settings.SkyZenithSunsetColor,  new Color(0.3f, 0.1f, 0.35f, 1.0f));
				TrySetColorPicker(_skyNightHorizonColorPicker,  settings.SkyNightHorizonColor,  new Color(0.05f, 0.05f, 0.15f, 1.0f));
				TrySetColorPicker(_skyNightZenithColorPicker,   settings.SkyNightZenithColor,   new Color(0.01f, 0.01f, 0.03f, 1.0f));
				TrySetColorPicker(_skyStarsColorPicker,         settings.SkyStarsColor,         new Color(1.0f, 1.0f, 1.0f, 1.0f));
				TrySetColorPicker(_cloudsColorPicker,           settings.CloudsColor,           new Color(1.0f, 1.0f, 1.0f, 1.0f));
				ApplyAllSkyColors();
				ApplyAllCloudsColors();
			}
		}
	}

	/// <summary>
	/// Resets sky colors and sun rotation to their defaults.
	/// </summary>
	private void ResetSkySettings()
	{
		if (_skyHorizonDayColorPicker != null)    _skyHorizonDayColorPicker.Color    = new Color(0.576f, 0.608f, 1.0f, 1.0f);
		if (_skyZenithDayColorPicker != null)     _skyZenithDayColorPicker.Color     = new Color(0.12f, 0.25f, 0.55f, 1.0f);
		if (_skyHorizonSunsetColorPicker != null) _skyHorizonSunsetColorPicker.Color = new Color(1.0f, 0.45f, 0.2f, 1.0f);
		if (_skyZenithSunsetColorPicker != null)  _skyZenithSunsetColorPicker.Color  = new Color(0.3f, 0.1f, 0.35f, 1.0f);
		if (_skyNightHorizonColorPicker != null)  _skyNightHorizonColorPicker.Color  = new Color(0.05f, 0.05f, 0.15f, 1.0f);
		if (_skyNightZenithColorPicker != null)   _skyNightZenithColorPicker.Color   = new Color(0.01f, 0.01f, 0.03f, 1.0f);
		if (_skyStarsColorPicker != null)         _skyStarsColorPicker.Color         = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		if (_cloudsColorPicker != null)           _cloudsColorPicker.Color           = new Color(1.0f, 1.0f, 1.0f, 1.0f);

		ApplyAllSkyColors();
		ApplyAllCloudsColors();

		_sunRotationXSpinBox?.SetValueNoSignal(0.0);
		_sunRotationYSpinBox?.SetValueNoSignal(0.0);
		_sunRotationZSpinBox?.SetValueNoSignal(0.0);
		ApplySunRotation();

		// Reset Advanced Sky settings
		ResetAdvancedSkySettings();
	}

	/// <summary>
	/// Sets a ColorPickerButton's color from an HTML string, falling back to <paramref name="defaultColor"/>
	/// if the string is null/empty or cannot be parsed.
	/// </summary>
	private static void TrySetColorPicker(ColorPickerButton picker, string htmlColor, Color defaultColor)
	{
		if (picker == null) return;
		if (!string.IsNullOrEmpty(htmlColor))
		{
			try { picker.Color = new Color(htmlColor); return; }
			catch { /* fall through to default */ }
		}
		picker.Color = defaultColor;
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
		// Try to load floor settings from the saved project data
		var settings = ProjectManager.GetSettings();
		bool hasSavedFloorSettings = settings != null;

		// Load floor visibility
		if (_floorNode != null)
		{
			// Use saved setting if available, otherwise use current scene state
			bool floorVisible = hasSavedFloorSettings ? settings.FloorVisible : _floorNode.Visible;
			_floorVisibilityCheckbox.SetPressedNoSignal(floorVisible);
			_floorNode.Visible = floorVisible;
		}

		// Set floor texture
		if (_blockTexturePaths != null && _blockTexturePaths.Count > 0)
		{
			string textureToUse;

			if (hasSavedFloorSettings && !string.IsNullOrEmpty(settings.FloorTexture))
			{
				// Use saved texture from project
				textureToUse = settings.FloorTexture;
			}
			else
			{
				// Default to grass_block_top
				textureToUse = "grass_block_top";
			}

			// Try to find the saved texture in available textures
			int textureIndex = _blockTexturePaths.FindIndex(p => p.Contains(textureToUse));

			if (textureIndex >= 0)
			{
				_floorTextureDropdown.Selected = textureIndex;
				ApplyFloorTexture(textureIndex);
			}
			else
			{
				// Fallback to grass_block_top if saved texture not found
				int grassIndex = _blockTexturePaths.FindIndex(p => p.Contains("grass_block_top"));
				if (grassIndex >= 0)
				{
					_floorTextureDropdown.Selected = grassIndex;
					ApplyFloorTexture(grassIndex);
				}
				else
				{
					// Fallback to first texture if nothing found
					_floorTextureDropdown.Selected = 0;
					ApplyFloorTexture(0);
				}
			}
		}
	}
	
	private void OnFloorVisibilityToggled(bool visible)
	{
		var old = _floorNode?.Visible ?? !visible;
		if (_floorNode != null)
		{
			_floorNode.Visible = visible;
		}

		if (old != visible && EditorCommandHistory.Instance != null)
		{
			var capturedOld = old; var capturedNew = visible;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<bool>(
					"Change Floor Visibility",
					capturedOld, capturedNew,
					v =>
					{
						_floorVisibilityCheckbox.SetPressedNoSignal(v);
						if (_floorNode != null) _floorNode.Visible = v;
					}));
		}
	}
	
	private void OnFloorTextureSelected(long index)
	{
		var oldIndex = (long)_floorTextureDropdown.Selected;
		ApplyFloorTexture(index);

		if (oldIndex != index && EditorCommandHistory.Instance != null)
		{
			var capturedOld = oldIndex; var capturedNew = index;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<long>(
					"Change Floor Texture",
					capturedOld, capturedNew,
					v =>
					{
						_floorTextureDropdown.Selected = (int)v;
						ApplyFloorTexture(v);
					}));
		}
	}

	private void ApplyFloorTexture(long index)
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
		// Save the new project name to the project data
		ProjectManager.SetProjectName(newName);
		
		// Update the window title to reflect the new project name
		Main.Instance?.UpdateWindowTitle();
	}
	
	private void OnResolutionChanged(double value)
	{
		// Actual logic is minimal (resolution is read at render time), but we still record undo
	}
	
	private void OnResolutionPresetPressed(int width, int height)
	{
		var oldW = _resolutionWidthSpinBox.Value;
		var oldH = _resolutionHeightSpinBox.Value;
		_resolutionWidthSpinBox.Value = width;
		_resolutionHeightSpinBox.Value = height;

		// Record undo as a single compound command via a tuple
		if (EditorCommandHistory.Instance != null && ((int)oldW != width || (int)oldH != height))
		{
			var capturedOldW = oldW; var capturedOldH = oldH;
			var capturedNewW = (double)width; var capturedNewH = (double)height;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<(double w, double h)>(
					"Change Resolution",
					(capturedOldW, capturedOldH),
					(capturedNewW, capturedNewH),
					v =>
					{
						_resolutionWidthSpinBox.SetValueNoSignal(v.w);
						_resolutionHeightSpinBox.SetValueNoSignal(v.h);
					}));
		}
	}
	
	private void OnFramerateChanged(double value)
	{
		// Actual logic is minimal (framerate is read at render time)
	}
	
	private void OnFrameratePresetPressed(int fps)
	{
		var oldFps = _framerateSpinBox.Value;
		_framerateSpinBox.Value = fps;

		if (EditorCommandHistory.Instance != null && (int)oldFps != fps)
		{
			var capturedOld = oldFps; var capturedNew = (double)fps;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<double>(
					"Change Framerate",
					capturedOld, capturedNew,
					v => _framerateSpinBox.SetValueNoSignal(v)));
		}
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
		var oldFps = _textureAnimationFpsSpinBox.Value;
		_textureAnimationFpsSpinBox.Value = fps;

		if (EditorCommandHistory.Instance != null && (int)oldFps != fps)
		{
			var capturedOld = oldFps; var capturedNew = (double)fps;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<double>(
					"Change Texture Animation FPS",
					capturedOld, capturedNew,
					v =>
					{
						_textureAnimationFpsSpinBox.SetValueNoSignal(v);
						if (AnimatedTextureManager.Instance != null)
							AnimatedTextureManager.Instance.SetTextureAnimationFps((float)v);
					}));
		}
	}

	// ── Project property undo helpers ────────────────────────────────────────

	/// <summary>
	/// Hooks focus-enter/exit on a SpinBox's internal LineEdit so we can capture
	/// the pre-edit value and record an undo command when the user finishes editing.
	/// </summary>
	private void HookSpinBoxUndo(SpinBox spinBox, Func<double> getPreEdit, Action<double> setPreEdit,
		string description, Action<double> applyValue)
	{
		spinBox.Ready += () =>
		{
			var lineEdit = spinBox.GetLineEdit();
			if (lineEdit == null) return;

			lineEdit.FocusEntered += () => setPreEdit(spinBox.Value);
			lineEdit.FocusExited += () =>
			{
				var pre = getPreEdit();
				var cur = spinBox.Value;
				if (Math.Abs(cur - pre) < 1e-9) return;
				if (EditorCommandHistory.Instance == null) return;
				var capturedPre = pre; var capturedCur = cur;
				EditorCommandHistory.Instance.PushWithoutExecute(
					new PropertyChangeCommand<double>(description, capturedPre, capturedCur, applyValue));
			};
		};
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

	/// <summary>
	/// Gets the current resolution width.
	/// </summary>
	public int GetResolutionWidth() => (int)_resolutionWidthSpinBox.Value;

	/// <summary>
	/// Gets the current resolution height.
	/// </summary>
	public int GetResolutionHeight() => (int)_resolutionHeightSpinBox.Value;
}

/// <summary>
/// Holds the current Minecraft sky shader color values for save/load.
/// All colors are stored as HTML strings (e.g. "#rrggbbaa").
/// </summary>
public class SkyShaderColors
{
	public string HorizonDayColor    { get; set; } = "";
	public string ZenithDayColor     { get; set; } = "";
	public string HorizonSunsetColor { get; set; } = "";
	public string ZenithSunsetColor  { get; set; } = "";
	public string NightHorizonColor  { get; set; } = "";
	public string NightZenithColor   { get; set; } = "";
	public string StarsColor         { get; set; } = "";
}

/// <summary>
/// Holds the current Advanced Sky atmosphere settings for save/load.
/// float.NaN means "use shader default".
/// </summary>
public class AdvancedSkySettings
{
	public float RayleighStrength  { get; set; } = float.NaN;
	public float MieStrength       { get; set; } = float.NaN;
	public float OzoneStrength     { get; set; } = float.NaN;
	public float AtmDensity        { get; set; } = float.NaN;
	public float Exposure          { get; set; } = float.NaN;
	public float SunDiscFeather    { get; set; } = float.NaN;
	public float SunDiscIntensity  { get; set; } = float.NaN;
	public float StarsExposure     { get; set; } = float.NaN;
}
