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
	}

	/// <summary>
	/// Called by Main after all Minecraft assets have been loaded.
	/// Populates any UI that depends on textures / JSON data.
	/// </summary>
	public void OnAssetsLoaded()
	{
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
		
		if (_backgroundColorMesh == null)
		{
			GD.PrintErr("Could not find BackgroundColor MeshInstance3D node");
		}
		
		if (_backgroundImageMesh == null)
		{
			GD.PrintErr("Could not find BackgroundImage MeshInstance3D node");
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
