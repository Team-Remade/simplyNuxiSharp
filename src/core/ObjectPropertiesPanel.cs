using Godot;
using System;
using System.Linq;
using simplyRemadeNuxi.core.commands;

namespace simplyRemadeNuxi.core;

public partial class ObjectPropertiesPanel : Panel
{
	/// <summary>Singleton reference set in <see cref="_Ready"/>.</summary>
	public static ObjectPropertiesPanel Instance { get; private set; }

	private VBoxContainer _vboxContainer;
	private Label _objectNameLabel;
	private CheckBox _visibilityCheckbox;
	private SpinBox _positionX;
	private SpinBox _positionY;
	private SpinBox _positionZ;
	private SpinBox _rotationX;
	private SpinBox _rotationY;
	private SpinBox _rotationZ;
	private SpinBox _scaleX;
	private SpinBox _scaleY;
	private SpinBox _scaleZ;
	private SpinBox _pivotOffsetX;
	private SpinBox _pivotOffsetY;
	private SpinBox _pivotOffsetZ;
	private CollapsibleSection _positionSection;
	private CollapsibleSection _rotationSection;
	private CollapsibleSection _scaleSection;
	private CollapsibleSection _pivotOffsetSection;
	private CheckBox _inheritPivotOffsetCheckbox;
	private CheckBox _inheritPositionCheckbox;
	private CheckBox _inheritRotationCheckbox;
	private CheckBox _inheritScaleCheckbox;
	private CheckBox _inheritVisibilityCheckbox;
	private CheckBox _castShadowCheckbox;
	private CheckBox _linkScaleCheckbox;
	private bool _isUpdatingScale = false;
	private CollapsibleSection _materialSection;
	private HSlider _materialAlphaSlider;
	private Label _materialAlphaLabel;
	private OptionButton _materialAlphaModeDropdown;
	private SceneObject _currentObject;
	
	// Material editor controls
	private ColorPickerButton _materialAlbedoPicker;
	private HSlider _materialMetallicSlider;
	private Label _materialMetallicLabel;
	private HSlider _materialRoughnessSlider;
	private Label _materialRoughnessLabel;
	private Button _materialNormalMapButton;
	private Label _materialNormalMapLabel;
	private CheckBox _materialEmissionEnabledCheckbox;
	private ColorPickerButton _materialEmissionPicker;
	private HSlider _materialEmissionEnergySlider;
	private Label _materialEmissionEnergyLabel;
	
	// Pre-edit state for material properties
	private Color _preEditAlbedoColor;
	private float _preEditMetallic;
	private float _preEditRoughness;
	private string _preEditNormalMapPath;
	private bool _preEditEmissionEnabled;
	private Color _preEditEmissionColor;
	private float _preEditEmissionEnergy;
	
	// Light-specific controls
	private CollapsibleSection _lightSection;
	private ColorPickerButton _lightColorPicker2;
	private SpinBox _lightEnergySpinBox;
	private SpinBox _lightRangeSpinBox;
	private SpinBox _lightIndirectEnergySpinBox;
	private SpinBox _lightSpecularSpinBox;
	private CheckBox _lightShadowCheckbox;

	// Bend-specific controls (shown only for bones with bend parameters)
	private CollapsibleSection _bendSection;
	private SpinBox _bendAngleX;
	private SpinBox _bendAngleY;
	private SpinBox _bendAngleZ;
	private Label _bendPartLabel;
	private Label _bendAxisLabel;
	
	// Store original values for reset functionality
	private Vector3 _originalPosition = Vector3.Zero;
	private Vector3 _originalRotation = Vector3.Zero;
	private Vector3 _originalScale = Vector3.One;
	private Vector3 _originalPivotOffset = new Vector3(0, -0.5f, 0);

	// Pre-edit state for transform undo/redo.
	// Captured when the object is selected; updated after each recorded command.
	private Vector3 _preEditPosition = Vector3.Zero;
	private Vector3 _preEditRotation = Vector3.Zero;
	private Vector3 _preEditScale = Vector3.One;
	private bool _suppressUndoRecord = false;

	// Pre-edit state for light property spinboxes
	private float _preEditLightEnergy;
	private float _preEditLightRange;
	private float _preEditLightIndirectEnergy;
	private float _preEditLightSpecular;
	private Color _preEditLightColor;

	// Pre-edit state for bend angle spinboxes
	private Vector3 _preEditBendAngle = Vector3.Zero;

	public override void _Ready()
	{
		Instance = this;
		SetupUi();
		SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
	}

	/// <summary>
	/// Re-reads the current object's values and updates all UI controls.
	/// Called by undo/redo commands after they apply a transform.
	/// </summary>
	public void RefreshFromObject()
	{
		UpdateUiFromObject();
	}

	public override void _ExitTree()
	{
		if (SelectionManager.Instance != null)
		{
			SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
		}
	}

	public override void _Process(double delta)
	{
		// Update UI continuously when gizmo is being used
		if (SelectionManager.Instance != null && SelectionManager.Instance.IsGizmoEditing && _currentObject != null)
		{
			UpdateUiFromObject();
		}
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

		// Object name label
		_objectNameLabel = new Label();
		_objectNameLabel.Name = "ObjectNameLabel";
		_objectNameLabel.Text = "No object selected";
		_objectNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_objectNameLabel);

		// Visibility checkbox
		var visibilityContainer = new HBoxContainer();
		vbox.AddChild(visibilityContainer);
		
		var visibilityLabel = new Label();
		visibilityLabel.Text = "Visible:";
		visibilityLabel.CustomMinimumSize = new Vector2(60, 0);
		visibilityContainer.AddChild(visibilityLabel);
		
		_visibilityCheckbox = new CheckBox();
		_visibilityCheckbox.Name = "VisibilityCheckbox";
		_visibilityCheckbox.Text = "";  // Empty text - we have a label already
		_visibilityCheckbox.ButtonPressed = true;
		_visibilityCheckbox.CustomMinimumSize = new Vector2(24, 24);  // Ensure it's visible
		// Add background color to make it visible when unchecked
		_visibilityCheckbox.AddThemeColorOverride("font_color", new Color(1, 1, 1));  // White checkmark
		// Add style box for background
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = new Color(0.25f, 0.25f, 0.25f);  // Slightly lighter background
		styleBox.BorderColor = new Color(0.5f, 0.5f, 0.5f);  // Border to make it visible
		styleBox.SetBorderWidthAll(1);
		styleBox.SetCornerRadiusAll(2);
		_visibilityCheckbox.AddThemeStyleboxOverride("normal", styleBox);
		_visibilityCheckbox.AddThemeStyleboxOverride("hover", styleBox);
		_visibilityCheckbox.AddThemeStyleboxOverride("pressed", styleBox);
		_visibilityCheckbox.Toggled += OnVisibilityChanged;
		visibilityContainer.AddChild(_visibilityCheckbox);

		// Inherit Visibility checkbox (checked by default, like position/rotation/scale)
		var inheritVisibilityRow = new HBoxContainer();
		vbox.AddChild(inheritVisibilityRow);
		var inheritVisibilityLabel = new Label();
		inheritVisibilityLabel.Text = "Inherit Visibility:";
		inheritVisibilityLabel.CustomMinimumSize = new Vector2(120, 0);
		inheritVisibilityRow.AddChild(inheritVisibilityLabel);
		_inheritVisibilityCheckbox = new CheckBox();
		_inheritVisibilityCheckbox.Name = "InheritVisibilityCheckbox";
		_inheritVisibilityCheckbox.Text = "";
		_inheritVisibilityCheckbox.ButtonPressed = true;
		_inheritVisibilityCheckbox.TooltipText = "When checked, this object inherits the parent's visibility";
		_inheritVisibilityCheckbox.Toggled += OnInheritVisibilityChanged;
		inheritVisibilityRow.AddChild(_inheritVisibilityCheckbox);

		// Cast Shadow checkbox
		var castShadowRow = new HBoxContainer();
		vbox.AddChild(castShadowRow);
		var castShadowLabel = new Label();
		castShadowLabel.Text = "Cast Shadows:";
		castShadowLabel.CustomMinimumSize = new Vector2(100, 0);
		castShadowRow.AddChild(castShadowLabel);
		_castShadowCheckbox = new CheckBox();
		_castShadowCheckbox.Name = "CastShadowCheckbox";
		_castShadowCheckbox.Text = "";
		_castShadowCheckbox.ButtonPressed = true;
		_castShadowCheckbox.TooltipText = "When checked, this object casts shadows";
		_castShadowCheckbox.Toggled += OnCastShadowChanged;
		castShadowRow.AddChild(_castShadowCheckbox);

		// Position section with toggle arrow
		_positionSection = new CollapsibleSection("Position");
		_positionSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_positionSection);
		_positionSection.GetResetButton().Pressed += OnResetPosition;
		
		var posContainer = _positionSection.GetContentContainer();

		// Inherit Position checkbox (checked by default)
		var inheritPositionRow = new HBoxContainer();
		posContainer.AddChild(inheritPositionRow);
		var inheritPositionLabel = new Label();
		inheritPositionLabel.Text = "Inherit Position:";
		inheritPositionLabel.CustomMinimumSize = new Vector2(120, 0);
		inheritPositionRow.AddChild(inheritPositionLabel);
		_inheritPositionCheckbox = new CheckBox();
		_inheritPositionCheckbox.Name = "InheritPositionCheckbox";
		_inheritPositionCheckbox.Text = "";
		_inheritPositionCheckbox.ButtonPressed = true;
		_inheritPositionCheckbox.TooltipText = "When checked, this object inherits the parent's position";
		_inheritPositionCheckbox.Toggled += OnInheritPositionChanged;
		inheritPositionRow.AddChild(_inheritPositionCheckbox);

		_positionX = CreateSpinBoxRow(posContainer, "X:", OnPositionChanged, OnTransformEditBegin, OnTransformEditEnd);
		_positionY = CreateSpinBoxRow(posContainer, "Y:", OnPositionChanged, OnTransformEditBegin, OnTransformEditEnd);
		_positionZ = CreateSpinBoxRow(posContainer, "Z:", OnPositionChanged, OnTransformEditBegin, OnTransformEditEnd);

		ConfigureSpinBox(_positionX);
		ConfigureSpinBox(_positionY);
		ConfigureSpinBox(_positionZ);

		// Rotation section with toggle arrow
		_rotationSection = new CollapsibleSection("Rotation (degrees)");
		_rotationSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_rotationSection);
		_rotationSection.GetResetButton().Pressed += OnResetRotation;
		
		var rotContainer = _rotationSection.GetContentContainer();

		// Inherit Rotation checkbox (checked by default)
		var inheritRotationRow = new HBoxContainer();
		rotContainer.AddChild(inheritRotationRow);
		var inheritRotationLabel = new Label();
		inheritRotationLabel.Text = "Inherit Rotation:";
		inheritRotationLabel.CustomMinimumSize = new Vector2(120, 0);
		inheritRotationRow.AddChild(inheritRotationLabel);
		_inheritRotationCheckbox = new CheckBox();
		_inheritRotationCheckbox.Name = "InheritRotationCheckbox";
		_inheritRotationCheckbox.Text = "";
		_inheritRotationCheckbox.ButtonPressed = true;
		_inheritRotationCheckbox.TooltipText = "When checked, this object inherits the parent's rotation";
		_inheritRotationCheckbox.Toggled += OnInheritRotationChanged;
		inheritRotationRow.AddChild(_inheritRotationCheckbox);

		_rotationX = CreateSpinBoxRow(rotContainer, "X:", OnRotationChanged, OnTransformEditBegin, OnTransformEditEnd);
		_rotationY = CreateSpinBoxRow(rotContainer, "Y:", OnRotationChanged, OnTransformEditBegin, OnTransformEditEnd);
		_rotationZ = CreateSpinBoxRow(rotContainer, "Z:", OnRotationChanged, OnTransformEditBegin, OnTransformEditEnd);

		// Scale section with toggle arrow
		_scaleSection = new CollapsibleSection("Scale");
		_scaleSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_scaleSection);
		_scaleSection.GetResetButton().Pressed += OnResetScale;
		
		var scaleContainer = _scaleSection.GetContentContainer();

		// Inherit Scale checkbox (checked by default)
		var inheritScaleRow = new HBoxContainer();
		scaleContainer.AddChild(inheritScaleRow);
		var inheritScaleLabel = new Label();
		inheritScaleLabel.Text = "Inherit Scale:";
		inheritScaleLabel.CustomMinimumSize = new Vector2(120, 0);
		inheritScaleRow.AddChild(inheritScaleLabel);
		_inheritScaleCheckbox = new CheckBox();
		_inheritScaleCheckbox.Name = "InheritScaleCheckbox";
		_inheritScaleCheckbox.Text = "";
		_inheritScaleCheckbox.ButtonPressed = true;
		_inheritScaleCheckbox.TooltipText = "When checked, this object inherits the parent's scale";
		_inheritScaleCheckbox.Toggled += OnInheritScaleChanged;
		inheritScaleRow.AddChild(_inheritScaleCheckbox);

		// Link Scale checkbox
		var linkScaleRow = new HBoxContainer();
		scaleContainer.AddChild(linkScaleRow);
		var linkScaleLabel = new Label();
		linkScaleLabel.Text = "Link Scale:";
		linkScaleLabel.CustomMinimumSize = new Vector2(120, 0);
		linkScaleRow.AddChild(linkScaleLabel);
		_linkScaleCheckbox = new CheckBox();
		_linkScaleCheckbox.Name = "LinkScaleCheckbox";
		_linkScaleCheckbox.Text = "";
		_linkScaleCheckbox.ButtonPressed = false;
		_linkScaleCheckbox.TooltipText = "When checked, changing one scale axis will proportionally update the others";
		linkScaleRow.AddChild(_linkScaleCheckbox);

		_scaleX = CreateSpinBoxRow(scaleContainer, "X:", () => OnScaleAxisChanged("x"), OnTransformEditBegin, OnTransformEditEnd);
		_scaleY = CreateSpinBoxRow(scaleContainer, "Y:", () => OnScaleAxisChanged("y"), OnTransformEditBegin, OnTransformEditEnd);
		_scaleZ = CreateSpinBoxRow(scaleContainer, "Z:", () => OnScaleAxisChanged("z"), OnTransformEditBegin, OnTransformEditEnd);

		// Configure scale spinboxes to allow smaller values (like 0.001) for precision scaling
		ConfigureScaleSpinBox(_scaleX);
		ConfigureScaleSpinBox(_scaleY);
		ConfigureScaleSpinBox(_scaleZ);

		// Pivot Offset section with toggle arrow (non-animated property)
		_pivotOffsetSection = new CollapsibleSection("Pivot Offset");
		_pivotOffsetSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_pivotOffsetSection);
		_pivotOffsetSection.GetResetButton().Pressed += OnResetPivotOffset;
		
		var pivotOffsetContainer = _pivotOffsetSection.GetContentContainer();

		// Inherit Pivot Offset checkbox (unchecked by default)
		var inheritPivotRow = new HBoxContainer();
		pivotOffsetContainer.AddChild(inheritPivotRow);
		var inheritPivotLabel = new Label();
		inheritPivotLabel.Text = "Inherit Pivot:";
		inheritPivotLabel.CustomMinimumSize = new Vector2(100, 0);
		inheritPivotRow.AddChild(inheritPivotLabel);
		_inheritPivotOffsetCheckbox = new CheckBox();
		_inheritPivotOffsetCheckbox.Name = "InheritPivotOffsetCheckbox";
		_inheritPivotOffsetCheckbox.Text = "";
		_inheritPivotOffsetCheckbox.ButtonPressed = false;
		_inheritPivotOffsetCheckbox.TooltipText = "When checked, this object accumulates the parent's pivot offset";
		_inheritPivotOffsetCheckbox.Toggled += OnInheritPivotOffsetChanged;
		inheritPivotRow.AddChild(_inheritPivotOffsetCheckbox);

		_pivotOffsetX = CreateSpinBoxRow(pivotOffsetContainer, "X:", OnPivotOffsetChanged);
		_pivotOffsetY = CreateSpinBoxRow(pivotOffsetContainer, "Y:", OnPivotOffsetChanged);
		_pivotOffsetZ = CreateSpinBoxRow(pivotOffsetContainer, "Z:", OnPivotOffsetChanged);

		// Material section with toggle arrow
		_materialSection = new CollapsibleSection("Material");
		_materialSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_materialSection);
		_materialSection.GetResetButton().Pressed += OnResetMaterialAlpha;
		
		var materialContainer = _materialSection.GetContentContainer();
		
		// Alpha slider row
		var alphaRow = new HBoxContainer();
		materialContainer.AddChild(alphaRow);
		
		var alphaLabel = new Label();
		alphaLabel.Text = "Alpha:";
		alphaLabel.CustomMinimumSize = new Vector2(50, 0);
		alphaRow.AddChild(alphaLabel);
		
		_materialAlphaSlider = new HSlider();
		_materialAlphaSlider.Name = "MaterialAlphaSlider";
		_materialAlphaSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialAlphaSlider.MinValue = 0.0;
		_materialAlphaSlider.MaxValue = 1.0;
		_materialAlphaSlider.Step = 0.01;
		_materialAlphaSlider.Value = 1.0;
		_materialAlphaSlider.ValueChanged += OnMaterialAlphaChanged;
		alphaRow.AddChild(_materialAlphaSlider);
		
		_materialAlphaLabel = new Label();
		_materialAlphaLabel.Text = "1.00";
		_materialAlphaLabel.CustomMinimumSize = new Vector2(40, 0);
		alphaRow.AddChild(_materialAlphaLabel);
		
		// Alpha mode dropdown row
		var alphaModeRow = new HBoxContainer();
		materialContainer.AddChild(alphaModeRow);
		
		var alphaModeLabel = new Label();
		alphaModeLabel.Text = "Alpha Mode:";
		alphaModeLabel.CustomMinimumSize = new Vector2(80, 0);
		alphaModeRow.AddChild(alphaModeLabel);
		
		_materialAlphaModeDropdown = new OptionButton();
		_materialAlphaModeDropdown.Name = "MaterialAlphaModeDropdown";
		_materialAlphaModeDropdown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialAlphaModeDropdown.AddItem("Disabled", (int)BaseMaterial3D.TransparencyEnum.Disabled);
		_materialAlphaModeDropdown.AddItem("Alpha", (int)BaseMaterial3D.TransparencyEnum.Alpha);
		_materialAlphaModeDropdown.AddItem("Alpha Scissor", (int)BaseMaterial3D.TransparencyEnum.AlphaScissor);
		_materialAlphaModeDropdown.AddItem("Alpha Hash", (int)BaseMaterial3D.TransparencyEnum.AlphaHash);
		_materialAlphaModeDropdown.AddItem("Depth Pre-Pass", (int)BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass);
		_materialAlphaModeDropdown.Selected = 0; // Default to Disabled
		_materialAlphaModeDropdown.ItemSelected += OnMaterialAlphaModeChanged;
		alphaModeRow.AddChild(_materialAlphaModeDropdown);

		// Separator
		var separator = new HSeparator();
		separator.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		materialContainer.AddChild(separator);

		// Albedo color row
		var albedoRow = new HBoxContainer();
		materialContainer.AddChild(albedoRow);
		var albedoLabel = new Label();
		albedoLabel.Text = "Albedo:";
		albedoLabel.CustomMinimumSize = new Vector2(60, 0);
		albedoRow.AddChild(albedoLabel);
		_materialAlbedoPicker = new ColorPickerButton();
		_materialAlbedoPicker.Name = "MaterialAlbedoPicker";
		_materialAlbedoPicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialAlbedoPicker.CustomMinimumSize = new Vector2(0, 30);
		_materialAlbedoPicker.EditAlpha = true;
		_materialAlbedoPicker.ColorChanged += OnMaterialAlbedoChanged;
		_materialAlbedoPicker.GetPopup().AboutToPopup += () => _preEditAlbedoColor = _materialAlbedoPicker.Color;
		_materialAlbedoPicker.GetPopup().PopupHide += () =>
		{
			var pre = _preEditAlbedoColor;
			var cur = _materialAlbedoPicker.Color;
			if (pre == cur || EditorCommandHistory.Instance == null) return;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<Godot.Color>(
					"Change Albedo Color", pre, cur,
					v => { _materialAlbedoPicker.Color = v; ApplyMaterialProperty("albedo", v); }));
		};
		albedoRow.AddChild(_materialAlbedoPicker);

		// Metallic slider row
		var metallicRow = new HBoxContainer();
		materialContainer.AddChild(metallicRow);
		var metallicLabel = new Label();
		metallicLabel.Text = "Metallic:";
		metallicLabel.CustomMinimumSize = new Vector2(60, 0);
		metallicRow.AddChild(metallicLabel);
		_materialMetallicSlider = new HSlider();
		_materialMetallicSlider.Name = "MaterialMetallicSlider";
		_materialMetallicSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialMetallicSlider.MinValue = 0.0;
		_materialMetallicSlider.MaxValue = 1.0;
		_materialMetallicSlider.Step = 0.01;
		_materialMetallicSlider.Value = 0.0;
		_materialMetallicSlider.ValueChanged += OnMaterialMetallicChanged;
		metallicRow.AddChild(_materialMetallicSlider);
		_materialMetallicLabel = new Label();
		_materialMetallicLabel.Text = "0.00";
		_materialMetallicLabel.CustomMinimumSize = new Vector2(40, 0);
		metallicRow.AddChild(_materialMetallicLabel);

		// Roughness slider row
		var roughnessRow = new HBoxContainer();
		materialContainer.AddChild(roughnessRow);
		var roughnessLabel = new Label();
		roughnessLabel.Text = "Roughness:";
		roughnessLabel.CustomMinimumSize = new Vector2(60, 0);
		roughnessRow.AddChild(roughnessLabel);
		_materialRoughnessSlider = new HSlider();
		_materialRoughnessSlider.Name = "MaterialRoughnessSlider";
		_materialRoughnessSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialRoughnessSlider.MinValue = 0.0;
		_materialRoughnessSlider.MaxValue = 1.0;
		_materialRoughnessSlider.Step = 0.01;
		_materialRoughnessSlider.Value = 0.5;
		_materialRoughnessSlider.ValueChanged += OnMaterialRoughnessChanged;
		roughnessRow.AddChild(_materialRoughnessSlider);
		_materialRoughnessLabel = new Label();
		_materialRoughnessLabel.Text = "0.50";
		_materialRoughnessLabel.CustomMinimumSize = new Vector2(40, 0);
		roughnessRow.AddChild(_materialRoughnessLabel);

		// Normal map row
		var normalMapRow = new HBoxContainer();
		materialContainer.AddChild(normalMapRow);
		var normalMapLabel = new Label();
		normalMapLabel.Text = "Normal:";
		normalMapLabel.CustomMinimumSize = new Vector2(60, 0);
		normalMapRow.AddChild(normalMapLabel);
		_materialNormalMapButton = new Button();
		_materialNormalMapButton.Name = "MaterialNormalMapButton";
		_materialNormalMapButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialNormalMapButton.Text = "None";
		_materialNormalMapButton.TooltipText = "Click to select a normal map texture";
		_materialNormalMapButton.Pressed += OnMaterialNormalMapPressed;
		normalMapRow.AddChild(_materialNormalMapButton);
		var clearNormalMapBtn = new Button();
		clearNormalMapBtn.Name = "ClearNormalMapButton";
		clearNormalMapBtn.Text = "X";
		clearNormalMapBtn.TooltipText = "Clear normal map";
		clearNormalMapBtn.Pressed += OnClearNormalMapPressed;
		normalMapRow.AddChild(clearNormalMapBtn);

		// Emission enabled checkbox
		var emissionEnabledRow = new HBoxContainer();
		materialContainer.AddChild(emissionEnabledRow);
		var emissionEnabledLabel = new Label();
		emissionEnabledLabel.Text = "Emission:";
		emissionEnabledLabel.CustomMinimumSize = new Vector2(60, 0);
		emissionEnabledRow.AddChild(emissionEnabledLabel);
		_materialEmissionEnabledCheckbox = new CheckBox();
		_materialEmissionEnabledCheckbox.Name = "MaterialEmissionEnabledCheckbox";
		_materialEmissionEnabledCheckbox.Text = "";
		_materialEmissionEnabledCheckbox.ButtonPressed = false;
		_materialEmissionEnabledCheckbox.TooltipText = "Enable emission";
		_materialEmissionEnabledCheckbox.Toggled += OnMaterialEmissionEnabledChanged;
		emissionEnabledRow.AddChild(_materialEmissionEnabledCheckbox);

		// Emission color picker
		var emissionColorRow = new HBoxContainer();
		materialContainer.AddChild(emissionColorRow);
		var emissionColorSpacer = new Label();
		emissionColorSpacer.CustomMinimumSize = new Vector2(60, 0);
		emissionColorRow.AddChild(emissionColorSpacer);
		_materialEmissionPicker = new ColorPickerButton();
		_materialEmissionPicker.Name = "MaterialEmissionPicker";
		_materialEmissionPicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialEmissionPicker.CustomMinimumSize = new Vector2(0, 30);
		_materialEmissionPicker.EditAlpha = false;
		_materialEmissionPicker.ColorChanged += OnMaterialEmissionColorChanged;
		_materialEmissionPicker.GetPopup().AboutToPopup += () => _preEditEmissionColor = _materialEmissionPicker.Color;
		_materialEmissionPicker.GetPopup().PopupHide += () =>
		{
			var pre = _preEditEmissionColor;
			var cur = _materialEmissionPicker.Color;
			if (pre == cur || EditorCommandHistory.Instance == null) return;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<Godot.Color>(
					"Change Emission Color", pre, cur,
					v => { _materialEmissionPicker.Color = v; ApplyMaterialProperty("emission_color", v); }));
		};
		emissionColorRow.AddChild(_materialEmissionPicker);

		// Emission energy slider
		var emissionEnergyRow = new HBoxContainer();
		materialContainer.AddChild(emissionEnergyRow);
		var emissionEnergyLabel = new Label();
		emissionEnergyLabel.Text = "Emission E.:";
		emissionEnergyLabel.CustomMinimumSize = new Vector2(60, 0);
		emissionEnergyRow.AddChild(emissionEnergyLabel);
		_materialEmissionEnergySlider = new HSlider();
		_materialEmissionEnergySlider.Name = "MaterialEmissionEnergySlider";
		_materialEmissionEnergySlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_materialEmissionEnergySlider.MinValue = 0.0;
		_materialEmissionEnergySlider.MaxValue = 10.0;
		_materialEmissionEnergySlider.Step = 0.1;
		_materialEmissionEnergySlider.Value = 1.0;
		_materialEmissionEnergySlider.ValueChanged += OnMaterialEmissionEnergyChanged;
		emissionEnergyRow.AddChild(_materialEmissionEnergySlider);
		_materialEmissionEnergyLabel = new Label();
		_materialEmissionEnergyLabel.Text = "1.00";
		_materialEmissionEnergyLabel.CustomMinimumSize = new Vector2(40, 0);
		emissionEnergyRow.AddChild(_materialEmissionEnergyLabel);

		// Light section (initially hidden, shown only for LightSceneObject)
		_lightSection = new CollapsibleSection("Light");
		_lightSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lightSection.Visible = false;
		vbox.AddChild(_lightSection);
		_lightSection.GetResetButton().Pressed += OnResetLightProperties;

		var lightContainer = _lightSection.GetContentContainer();

		// Color row (reuse the existing color picker but also show it here in the section)
		var lightColorRow = new HBoxContainer();
		lightContainer.AddChild(lightColorRow);
		var lightColorLabel = new Label();
		lightColorLabel.Text = "Color:";
		lightColorLabel.CustomMinimumSize = new Vector2(90, 0);
		lightColorRow.AddChild(lightColorLabel);
		var lightColorPicker2 = new ColorPickerButton();
		lightColorPicker2.Name = "LightColorPicker2";
		lightColorPicker2.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lightColorPicker2.CustomMinimumSize = new Vector2(0, 30);
		lightColorPicker2.EditAlpha = false;
		lightColorPicker2.ColorChanged += (color) =>
		{
			if (_currentObject is LightSceneObject lo)
			{
				lo.LightColor = color;
				AutoKeyframe("light.color.r");
				AutoKeyframe("light.color.g");
				AutoKeyframe("light.color.b");
			}
		};
		// Capture pre-edit color when popup opens; record undo when it closes
		lightColorPicker2.Ready += () =>
		{
			lightColorPicker2.GetPopup().AboutToPopup += () =>
				_preEditLightColor = lightColorPicker2.Color;
			lightColorPicker2.GetPopup().PopupHide += () =>
			{
				var pre = _preEditLightColor;
				var cur = lightColorPicker2.Color;
				if (pre == cur || EditorCommandHistory.Instance == null) return;
				EditorCommandHistory.Instance.PushWithoutExecute(
					new PropertyChangeCommand<Godot.Color>(
						"Change Light Color", pre, cur,
						v =>
						{
							if (_currentObject is LightSceneObject lo2) lo2.LightColor = v;
							_lightColorPicker2.Color = v;
						}));
			};
		};
		lightColorRow.AddChild(lightColorPicker2);
		// Store reference so we can update it
		_lightColorPicker2 = lightColorPicker2;

		// Energy row
		var energyRow = new HBoxContainer();
		lightContainer.AddChild(energyRow);
		var energyLabel = new Label();
		energyLabel.Text = "Energy:";
		energyLabel.CustomMinimumSize = new Vector2(90, 0);
		energyRow.AddChild(energyLabel);
		_lightEnergySpinBox = new SpinBox();
		_lightEnergySpinBox.Name = "LightEnergySpinBox";
		_lightEnergySpinBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lightEnergySpinBox.MinValue = 0.0;
		_lightEnergySpinBox.MaxValue = 100.0;
		_lightEnergySpinBox.Step = 0.1;
		_lightEnergySpinBox.Value = 1.0;
		_lightEnergySpinBox.TooltipText = "Light brightness/intensity";
		_lightEnergySpinBox.ValueChanged += OnLightEnergyChanged;
		energyRow.AddChild(_lightEnergySpinBox);
		HookLightSpinBoxUndo(_lightEnergySpinBox,
			() => _preEditLightEnergy, v => _preEditLightEnergy = v,
			"Change Light Energy",
			v => { if (_currentObject is LightSceneObject lo) lo.LightEnergy = v; });

		// Range row
		var rangeRow = new HBoxContainer();
		lightContainer.AddChild(rangeRow);
		var rangeLabelCtrl = new Label();
		rangeLabelCtrl.Text = "Range:";
		rangeLabelCtrl.CustomMinimumSize = new Vector2(90, 0);
		rangeRow.AddChild(rangeLabelCtrl);
		_lightRangeSpinBox = new SpinBox();
		_lightRangeSpinBox.Name = "LightRangeSpinBox";
		_lightRangeSpinBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lightRangeSpinBox.MinValue = 0.01;
		_lightRangeSpinBox.MaxValue = 500.0;
		_lightRangeSpinBox.Step = 0.1;
		_lightRangeSpinBox.Value = 5.0;
		_lightRangeSpinBox.TooltipText = "Radius of the light's influence";
		_lightRangeSpinBox.ValueChanged += OnLightRangeChanged;
		rangeRow.AddChild(_lightRangeSpinBox);
		HookLightSpinBoxUndo(_lightRangeSpinBox,
			() => _preEditLightRange, v => _preEditLightRange = v,
			"Change Light Range",
			v => { if (_currentObject is LightSceneObject lo) lo.LightRange = v; });

		// Indirect Energy row
		var indirectRow = new HBoxContainer();
		lightContainer.AddChild(indirectRow);
		var indirectLabel = new Label();
		indirectLabel.Text = "Indirect Energy:";
		indirectLabel.CustomMinimumSize = new Vector2(90, 0);
		indirectRow.AddChild(indirectLabel);
		_lightIndirectEnergySpinBox = new SpinBox();
		_lightIndirectEnergySpinBox.Name = "LightIndirectEnergySpinBox";
		_lightIndirectEnergySpinBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lightIndirectEnergySpinBox.MinValue = 0.0;
		_lightIndirectEnergySpinBox.MaxValue = 16.0;
		_lightIndirectEnergySpinBox.Step = 0.1;
		_lightIndirectEnergySpinBox.Value = 1.0;
		_lightIndirectEnergySpinBox.TooltipText = "Contribution to global illumination";
		_lightIndirectEnergySpinBox.ValueChanged += OnLightIndirectEnergyChanged;
		indirectRow.AddChild(_lightIndirectEnergySpinBox);
		HookLightSpinBoxUndo(_lightIndirectEnergySpinBox,
			() => _preEditLightIndirectEnergy, v => _preEditLightIndirectEnergy = v,
			"Change Light Indirect Energy",
			v => { if (_currentObject is LightSceneObject lo) lo.LightIndirectEnergy = v; });

		// Specular row
		var specularRow = new HBoxContainer();
		lightContainer.AddChild(specularRow);
		var specularLabel = new Label();
		specularLabel.Text = "Specular:";
		specularLabel.CustomMinimumSize = new Vector2(90, 0);
		specularRow.AddChild(specularLabel);
		_lightSpecularSpinBox = new SpinBox();
		_lightSpecularSpinBox.Name = "LightSpecularSpinBox";
		_lightSpecularSpinBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lightSpecularSpinBox.MinValue = 0.0;
		_lightSpecularSpinBox.MaxValue = 1.0;
		_lightSpecularSpinBox.Step = 0.01;
		_lightSpecularSpinBox.Value = 0.5;
		_lightSpecularSpinBox.TooltipText = "Specular highlight intensity (0 = none, 1 = full)";
		_lightSpecularSpinBox.ValueChanged += OnLightSpecularChanged;
		specularRow.AddChild(_lightSpecularSpinBox);
		HookLightSpinBoxUndo(_lightSpecularSpinBox,
			() => _preEditLightSpecular, v => _preEditLightSpecular = v,
			"Change Light Specular",
			v => { if (_currentObject is LightSceneObject lo) lo.LightSpecular = v; });

		// Shadow checkbox row
		var shadowRow = new HBoxContainer();
		lightContainer.AddChild(shadowRow);
		var shadowLabel = new Label();
		shadowLabel.Text = "Cast Shadows:";
		shadowLabel.CustomMinimumSize = new Vector2(90, 0);
		shadowRow.AddChild(shadowLabel);
		_lightShadowCheckbox = new CheckBox();
		_lightShadowCheckbox.Name = "LightShadowCheckbox";
		_lightShadowCheckbox.Text = "";
		_lightShadowCheckbox.ButtonPressed = true;
		_lightShadowCheckbox.TooltipText = "Enable shadow casting";
		_lightShadowCheckbox.Toggled += OnLightShadowToggled;
		shadowRow.AddChild(_lightShadowCheckbox);

		// Bend section (initially hidden, shown only for bones with bend parameters)
		_bendSection = new CollapsibleSection("Bend");
		_bendSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_bendSection.Visible = false;
		vbox.AddChild(_bendSection);
		_bendSection.GetResetButton().Pressed += OnResetBendAngle;

		var bendContainer = _bendSection.GetContentContainer();

		// Info row: part direction
		var bendInfoRow = new HBoxContainer();
		bendContainer.AddChild(bendInfoRow);
		var bendPartLabelTitle = new Label();
		bendPartLabelTitle.Text = "Part:";
		bendPartLabelTitle.CustomMinimumSize = new Vector2(60, 0);
		bendInfoRow.AddChild(bendPartLabelTitle);
		_bendPartLabel = new Label();
		_bendPartLabel.Text = "-";
		_bendPartLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bendInfoRow.AddChild(_bendPartLabel);

		// Info row: active axes
		var bendAxisRow = new HBoxContainer();
		bendContainer.AddChild(bendAxisRow);
		var bendAxisLabelTitle = new Label();
		bendAxisLabelTitle.Text = "Axes:";
		bendAxisLabelTitle.CustomMinimumSize = new Vector2(60, 0);
		bendAxisRow.AddChild(bendAxisLabelTitle);
		_bendAxisLabel = new Label();
		_bendAxisLabel.Text = "-";
		_bendAxisLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bendAxisRow.AddChild(_bendAxisLabel);

		// Angle spinboxes
		_bendAngleX = CreateSpinBoxRow(bendContainer, "X:", OnBendAngleChanged, OnBendEditBegin, OnBendEditEnd);
		_bendAngleY = CreateSpinBoxRow(bendContainer, "Y:", OnBendAngleChanged, OnBendEditBegin, OnBendEditEnd);
		_bendAngleZ = CreateSpinBoxRow(bendContainer, "Z:", OnBendAngleChanged, OnBendEditBegin, OnBendEditEnd);

		ConfigureSpinBox(_bendAngleX);
		ConfigureSpinBox(_bendAngleY);
		ConfigureSpinBox(_bendAngleZ);
	}

	private SpinBox CreateSpinBoxRow(VBoxContainer parent, string labelText, Action onChanged,
		Action onFocusEntered = null, Action onFocusExited = null)
	{
		var row = new HBoxContainer();
		parent.AddChild(row);
		
		var label = new Label();
		label.Text = labelText;
		label.CustomMinimumSize = new Vector2(20, 0);
		row.AddChild(label);

		var spin = new SpinBox();
		spin.Name = "SpinBox";
		spin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		spin.Step = 0.1;
		spin.MinValue = -10000;
		spin.MaxValue = 10000;
		spin.ValueChanged += (val) => onChanged?.Invoke();

		// Hook focus events on the internal LineEdit so we can capture pre-edit state
		spin.Ready += () =>
		{
			var lineEdit = spin.GetLineEdit();
			if (lineEdit != null)
			{
				if (onFocusEntered != null)
					lineEdit.FocusEntered += () => onFocusEntered();
				if (onFocusExited != null)
					lineEdit.FocusExited += () => onFocusExited();
			}
		};

		row.AddChild(spin);

		return spin;
	}

	private void ConfigureSpinBox(SpinBox spin)
	{
		spin.Step = 0.01;
		spin.MaxValue = 100000;
		spin.Rounded = false;
	}

	private void ConfigureScaleSpinBox(SpinBox spin)
	{
		spin.Step = 0.001;
		spin.MinValue = 0.001;
		spin.MaxValue = 100000;
		spin.Rounded = false;
	}

	private void OnSelectionChanged()
	{
		var selectedObjects = SelectionManager.Instance.SelectedObjects;
		if (selectedObjects.Count > 0)
		{
			_currentObject = selectedObjects[0];
			UpdateUiFromObject();
		}
		else
		{
			_currentObject = null;
			_objectNameLabel.Text = "No object selected";
			_lightSection.Visible = false;
			ClearSpinBoxes();
		}
	}

	private void UpdateUiFromObject()
	{
		if (_currentObject == null) return;

		_objectNameLabel.Text = _currentObject.Name;

		// Visibility
		_visibilityCheckbox.SetPressedNoSignal(_currentObject.ObjectVisible);

		// Show light section only for lights
		if (_currentObject is LightSceneObject lightObj)
		{
			// Show light section and populate it
			_lightSection.Visible = true;
			_lightColorPicker2.Color = lightObj.LightColor;
			_lightEnergySpinBox.SetValueNoSignal(Math.Round(lightObj.LightEnergy, 3));
			_lightRangeSpinBox.SetValueNoSignal(Math.Round(lightObj.LightRange, 3));
			_lightIndirectEnergySpinBox.SetValueNoSignal(Math.Round(lightObj.LightIndirectEnergy, 3));
			_lightSpecularSpinBox.SetValueNoSignal(Math.Round(lightObj.LightSpecular, 3));
			_lightShadowCheckbox.SetPressedNoSignal(lightObj.LightShadowEnabled);
		}
		else
		{
			_lightSection.Visible = false;
		}

		// For bones, show TargetPosition and TargetRotation (offset from base pose)
		// For other objects, show local position/rotation (before inheritance is applied)
		Vector3 pos, rot, scale;
		if (_currentObject is BoneSceneObject boneObj)
		{
			pos = boneObj.TargetPosition;
			rot = boneObj.TargetRotation;
			scale = _currentObject.Scale;
		}
		else
		{
			pos = _currentObject.LocalPosition;
			rot = _currentObject.LocalRotation;
			scale = _currentObject.LocalScale;
		}

		// Position (scaled by 16 for display) - use SetValueNoSignal to avoid triggering auto-keyframing
		_positionX.SetValueNoSignal(Math.Round(pos.X * 16, 2));
		_positionY.SetValueNoSignal(Math.Round(pos.Y * 16, 2));
		_positionZ.SetValueNoSignal(Math.Round(pos.Z * 16, 2));

		// Rotation (convert from radians to degrees) - use SetValueNoSignal to avoid triggering auto-keyframing
		_rotationX.SetValueNoSignal(Math.Round(Mathf.RadToDeg(rot.X), 2));
		_rotationY.SetValueNoSignal(Math.Round(Mathf.RadToDeg(rot.Y), 2));
		_rotationZ.SetValueNoSignal(Math.Round(Mathf.RadToDeg(rot.Z), 2));

		// Scale - use SetValueNoSignal to avoid triggering auto-keyframing
		_scaleX.SetValueNoSignal(Math.Round(scale.X, 2));
		_scaleY.SetValueNoSignal(Math.Round(scale.Y, 2));
		_scaleZ.SetValueNoSignal(Math.Round(scale.Z, 2));

		// Pivot Offset (scaled by 16 for display, same as position)
		var pivotOffset = _currentObject.PivotOffset;
		_pivotOffsetX.SetValueNoSignal(Math.Round(pivotOffset.X * 16, 2));
		_pivotOffsetY.SetValueNoSignal(Math.Round(pivotOffset.Y * 16, 2));
		_pivotOffsetZ.SetValueNoSignal(Math.Round(pivotOffset.Z * 16, 2));

		// Inheritance checkboxes
		_inheritPivotOffsetCheckbox.SetPressedNoSignal(_currentObject.InheritPivotOffset);
		_inheritPositionCheckbox.SetPressedNoSignal(_currentObject.InheritPosition);
		_inheritRotationCheckbox.SetPressedNoSignal(_currentObject.InheritRotation);
		_inheritScaleCheckbox.SetPressedNoSignal(_currentObject.InheritScale);
		_inheritVisibilityCheckbox.SetPressedNoSignal(_currentObject.InheritVisibility);
		_castShadowCheckbox.SetPressedNoSignal(_currentObject.CastShadow == GeometryInstance3D.ShadowCastingSetting.On);

		// Material Alpha - special handling for bones
		if (_currentObject is BoneSceneObject boneObject)
		{
			if (boneObject.ControlsSingleMesh())
			{
				// Show the bone's alpha override
				_materialAlphaSlider.SetValueNoSignal(boneObject.AlphaOverride);
				_materialAlphaLabel.Text = boneObject.AlphaOverride.ToString("F2");
			}
			else
			{
				// This bone is part of a skinned mesh - alpha not applicable here
				_materialAlphaSlider.SetValueNoSignal(1.0);
				_materialAlphaLabel.Text = "N/A";
			}

			// Bend section - show only if this bone has bend parameters
			if (boneObject.BendParameters.HasValue)
			{
				_bendSection.Visible = true;
				var bp = boneObject.BendParameters.Value;

				// Show part direction
				_bendPartLabel.Text = bp.Part.ToString();

				// Show active axes
				var axes = new System.Text.StringBuilder();
				if (bp.AxisX) axes.Append("X ");
				if (bp.AxisY) axes.Append("Y ");
				if (bp.AxisZ) axes.Append("Z ");
				_bendAxisLabel.Text = axes.Length > 0 ? axes.ToString().Trim() : "none";

				// Populate angle spinboxes
				_bendAngleX.SetValueNoSignal(Math.Round(bp.Angle.X, 2));
				_bendAngleY.SetValueNoSignal(Math.Round(bp.Angle.Y, 2));
				_bendAngleZ.SetValueNoSignal(Math.Round(bp.Angle.Z, 2));

				// Disable axes that are not active
				_bendAngleX.Editable = bp.AxisX;
				_bendAngleY.Editable = bp.AxisY;
				_bendAngleZ.Editable = bp.AxisZ;
			}
			else
			{
				_bendSection.Visible = false;
			}
		}
		else
		{
			// Material Alpha - get from first surface material if available
			var meshInstances = _currentObject.GetMeshInstancesRecursively(_currentObject.Visual);
			if (meshInstances.Count > 0 && meshInstances[0].Mesh != null && meshInstances[0].Mesh.GetSurfaceCount() > 0)
			{
				var material = meshInstances[0].Mesh.SurfaceGetMaterial(0);
				if (material is StandardMaterial3D stdMat)
				{
					var alpha = stdMat.AlbedoColor.A;
					_materialAlphaSlider.SetValueNoSignal(alpha);
					_materialAlphaLabel.Text = alpha.ToString("F2");
					
					// Update alpha mode dropdown to show current mode
					var transparencyMode = stdMat.Transparency;
					_materialAlphaModeDropdown.Selected = (int)transparencyMode;

					// Update material editor controls
					_materialAlbedoPicker.Color = stdMat.AlbedoColor;
					_materialMetallicSlider.SetValueNoSignal(stdMat.Metallic);
					_materialMetallicLabel.Text = stdMat.Metallic.ToString("F2");
					_materialRoughnessSlider.SetValueNoSignal(stdMat.Roughness);
					_materialRoughnessLabel.Text = stdMat.Roughness.ToString("F2");

					// Normal map
					if (stdMat.NormalTexture != null)
					{
						_materialNormalMapButton.Text = stdMat.NormalTexture.ResourcePath != null 
							? System.IO.Path.GetFileName(stdMat.NormalTexture.ResourcePath) 
							: "Normal Map";
					}
					else
					{
						_materialNormalMapButton.Text = "None";
					}

					// Emission
					_materialEmissionEnabledCheckbox.SetPressedNoSignal(stdMat.EmissionEnabled);
					_materialEmissionPicker.Color = stdMat.Emission;
					_materialEmissionEnergySlider.SetValueNoSignal(stdMat.EmissionEnergyMultiplier);
					_materialEmissionEnergyLabel.Text = stdMat.EmissionEnergyMultiplier.ToString("F2");
				}
			}
			else
			{
				_materialAlphaSlider.SetValueNoSignal(1.0);
				_materialAlphaLabel.Text = "1.00";
				_materialAlphaModeDropdown.Selected = 0; // Default to Disabled

				// Reset material editor controls to defaults
				_materialAlbedoPicker.Color = Colors.White;
				_materialMetallicSlider.SetValueNoSignal(0.0);
				_materialMetallicLabel.Text = "0.00";
				_materialRoughnessSlider.SetValueNoSignal(0.5);
				_materialRoughnessLabel.Text = "0.50";
				_materialNormalMapButton.Text = "None";
				_materialEmissionEnabledCheckbox.SetPressedNoSignal(false);
				_materialEmissionPicker.Color = Colors.White;
				_materialEmissionEnergySlider.SetValueNoSignal(1.0);
				_materialEmissionEnergyLabel.Text = "1.00";
			}

			_bendSection.Visible = false;
		}
	}

	private void ClearSpinBoxes()
	{
		_visibilityCheckbox.SetPressedNoSignal(true);
		_positionX.SetValueNoSignal(0);
		_positionY.SetValueNoSignal(0);
		_positionZ.SetValueNoSignal(0);
		_rotationX.SetValueNoSignal(0);
		_rotationY.SetValueNoSignal(0);
		_rotationZ.SetValueNoSignal(0);
		_scaleX.SetValueNoSignal(1);
		_scaleY.SetValueNoSignal(1);
		_scaleZ.SetValueNoSignal(1);
		_pivotOffsetX.SetValueNoSignal(0);
		_pivotOffsetY.SetValueNoSignal(0);
		_pivotOffsetZ.SetValueNoSignal(0);
		// Reset inheritance checkboxes to defaults
		_inheritPivotOffsetCheckbox.SetPressedNoSignal(false);
		_inheritPositionCheckbox.SetPressedNoSignal(true);
		_inheritRotationCheckbox.SetPressedNoSignal(true);
		_inheritScaleCheckbox.SetPressedNoSignal(true);
		_inheritVisibilityCheckbox.SetPressedNoSignal(true);
		_castShadowCheckbox.SetPressedNoSignal(true);

		// Reset material editor controls to defaults
		_materialAlphaSlider.SetValueNoSignal(1.0);
		_materialAlphaLabel.Text = "1.00";
		_materialAlphaModeDropdown.Selected = 0;
		_materialAlbedoPicker.Color = Colors.White;
		_materialMetallicSlider.SetValueNoSignal(0.0);
		_materialMetallicLabel.Text = "0.00";
		_materialRoughnessSlider.SetValueNoSignal(0.5);
		_materialRoughnessLabel.Text = "0.50";
		_materialNormalMapButton.Text = "None";
		_materialEmissionEnabledCheckbox.SetPressedNoSignal(false);
		_materialEmissionPicker.Color = Colors.White;
		_materialEmissionEnergySlider.SetValueNoSignal(1.0);
		_materialEmissionEnergyLabel.Text = "1.00";
		_inheritPositionCheckbox.SetPressedNoSignal(true);
		_inheritRotationCheckbox.SetPressedNoSignal(true);
		_inheritScaleCheckbox.SetPressedNoSignal(true);
		_inheritVisibilityCheckbox.SetPressedNoSignal(true);
		_castShadowCheckbox.SetPressedNoSignal(true);
		// Clear light controls
		_lightEnergySpinBox.SetValueNoSignal(1.0);
		_lightRangeSpinBox.SetValueNoSignal(5.0);
		_lightIndirectEnergySpinBox.SetValueNoSignal(1.0);
		_lightSpecularSpinBox.SetValueNoSignal(0.5);
		_lightShadowCheckbox.SetPressedNoSignal(true);
		// Hide bend section
		_bendSection.Visible = false;
		_bendAngleX.SetValueNoSignal(0);
		_bendAngleY.SetValueNoSignal(0);
		_bendAngleZ.SetValueNoSignal(0);
	}

	private void OnVisibilityChanged(bool visible)
	{
		if (_currentObject == null || _suppressUndoRecord) return;

		var oldVisible = _currentObject.ObjectVisible;
		_currentObject.SetObjectVisible(visible);

		// Record undo command
		if (EditorCommandHistory.Instance != null && oldVisible != visible)
		{
			EditorCommandHistory.Instance.PushWithoutExecute(
				new VisibilityCommand(_currentObject, oldVisible, visible));
		}

		// Auto-keyframe when property changes
		AutoKeyframe("visible");
	}

	private void OnPositionChanged()
	{
		if (_currentObject == null) return;

		var newPos = new Vector3(
			(float)_positionX.Value / 16,
			(float)_positionY.Value / 16,
			(float)_positionZ.Value / 16
		);

		// For bones, update TargetPosition (offset from base pose)
		// For other objects, update regular Position
		if (_currentObject is BoneSceneObject boneObj)
		{
			boneObj.TargetPosition = newPos;
		}
		else
		{
			_currentObject.SetLocalPosition(newPos);
		}

		// Record undo command
		RecordTransformCommand("Change Position");
		
		// Auto-keyframe when property changes
		AutoKeyframe("position.x");
		AutoKeyframe("position.y");
		AutoKeyframe("position.z");
	}

	private void OnRotationChanged()
	{
		if (_currentObject == null) return;

		var newRot = new Vector3(
			Mathf.DegToRad((float)_rotationX.Value),
			Mathf.DegToRad((float)_rotationY.Value),
			Mathf.DegToRad((float)_rotationZ.Value)
		);

		// For bones, update TargetRotation (offset from base pose)
		// For other objects, update regular Rotation
		if (_currentObject is BoneSceneObject boneObj)
		{
			boneObj.TargetRotation = newRot;
		}
		else
		{
			_currentObject.SetLocalRotation(newRot);
		}

		// Record undo command
		RecordTransformCommand("Change Rotation");
		
		// Auto-keyframe when property changes
		AutoKeyframe("rotation.x");
		AutoKeyframe("rotation.y");
		AutoKeyframe("rotation.z");
	}

	private void OnScaleAxisChanged(string changedAxis)
	{
		if (_currentObject == null || _isUpdatingScale) return;

		// Capture old scale before making any changes
		var oldScale = _currentObject.LocalScale;
		float oldX = oldScale.X;
		float oldY = oldScale.Y;
		float oldZ = oldScale.Z;

		// Get new values from the UI
		float newX = (float)_scaleX.Value;
		float newY = (float)_scaleY.Value;
		float newZ = (float)_scaleZ.Value;

		// If linked, apply the delta to the other axes
		if (_linkScaleCheckbox.ButtonPressed)
		{
			_isUpdatingScale = true;

			switch (changedAxis)
			{
				case "x":
					float deltaX = newX - oldX;
					newY = oldY + deltaX;
					newZ = oldZ + deltaX;
					break;
				case "y":
					float deltaY = newY - oldY;
					newX = oldX + deltaY;
					newZ = oldZ + deltaY;
					break;
				case "z":
					float deltaZ = newZ - oldZ;
					newX = oldX + deltaZ;
					newY = oldY + deltaZ;
					break;
			}

			// Update the Y and Z spinboxes if X was changed, etc.
			// Use SetValueNoSignal to avoid triggering this method again
			if (changedAxis == "x")
			{
				_scaleY.SetValueNoSignal(Math.Round(newY, 2));
				_scaleZ.SetValueNoSignal(Math.Round(newZ, 2));
			}
			else if (changedAxis == "y")
			{
				_scaleX.SetValueNoSignal(Math.Round(newX, 2));
				_scaleZ.SetValueNoSignal(Math.Round(newZ, 2));
			}
			else if (changedAxis == "z")
			{
				_scaleX.SetValueNoSignal(Math.Round(newX, 2));
				_scaleY.SetValueNoSignal(Math.Round(newY, 2));
			}

			_isUpdatingScale = false;
		}

		// Apply the new scale
		_currentObject.SetLocalScale(new Vector3(newX, newY, newZ));

		// Record undo command
		RecordTransformCommand("Change Scale");

		// Auto-keyframe when property changes
		AutoKeyframe("scale.x");
		AutoKeyframe("scale.y");
		AutoKeyframe("scale.z");
	}

	private void OnScaleChanged()
	{
		if (_currentObject == null || _suppressUndoRecord) return;

		_currentObject.SetLocalScale(new Vector3(
			(float)_scaleX.Value,
			(float)_scaleY.Value,
			(float)_scaleZ.Value
		));

		// Record undo command
		RecordTransformCommand("Change Scale");
		
		// Auto-keyframe when property changes
		AutoKeyframe("scale.x");
		AutoKeyframe("scale.y");
		AutoKeyframe("scale.z");
	}

	private void OnPivotOffsetChanged()
	{
		if (_currentObject == null) return;

		var newPivot = new Vector3(
			(float)_pivotOffsetX.Value / 16,
			(float)_pivotOffsetY.Value / 16,
			(float)_pivotOffsetZ.Value / 16
		);

		var oldPivot = _currentObject.PivotOffset;
		_currentObject.PivotOffset = newPivot;

		// Record undo command if the value actually changed
		if (EditorCommandHistory.Instance != null && newPivot != oldPivot)
		{
			var capturedObj = _currentObject;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<Godot.Vector3>(
					"Change Pivot Offset",
					oldPivot, newPivot,
					v =>
					{
						capturedObj.PivotOffset = v;
						if (Instance != null && SelectionManager.Instance != null &&
							SelectionManager.Instance.SelectedObjects.Contains(capturedObj))
							Instance.RefreshFromObject();
					}));
		}

		// Note: Pivot offset is NOT auto-keyframed as it's a non-animated property
	}

	private void OnInheritPivotOffsetChanged(bool inherit)
	{
		if (_currentObject == null) return;
		var old = _currentObject.InheritPivotOffset;
		_currentObject.InheritPivotOffset = inherit;
		RecordBoolPropertyCommand("Change Inherit Pivot Offset", old, inherit,
			v => { _currentObject.InheritPivotOffset = v; _inheritPivotOffsetCheckbox.SetPressedNoSignal(v); });
	}

	private void OnInheritVisibilityChanged(bool inherit)
	{
		if (_currentObject == null) return;
		var old = _currentObject.InheritVisibility;
		_currentObject.InheritVisibility = inherit;
		RecordBoolPropertyCommand("Change Inherit Visibility", old, inherit,
			v => { _currentObject.InheritVisibility = v; _inheritVisibilityCheckbox.SetPressedNoSignal(v); });
	}

	private void OnCastShadowChanged(bool castShadow)
	{
		if (_currentObject == null) return;
		var old = _currentObject.CastShadow == GeometryInstance3D.ShadowCastingSetting.On;
		_currentObject.CastShadow = castShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
		RecordBoolPropertyCommand("Change Cast Shadow", old, castShadow,
			v => { _currentObject.CastShadow = v ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off; _castShadowCheckbox.SetPressedNoSignal(v); });
	}

	/// <summary>
	/// Called when the user starts editing a transform field (position, rotation, scale).
	/// Captures the current transform values so we can undo all changes made during
	/// this edit session as a single command.
	/// </summary>
	private void OnTransformEditBegin()
	{
		if (_currentObject == null) return;

		// Capture the current transform as the baseline for this edit session
		_preEditPosition = _currentObject.LocalPosition;
		_preEditRotation = _currentObject.LocalRotation;
		_preEditScale = _currentObject.LocalScale;
	}

	/// <summary>
	/// Called when the user finishes editing a transform field.
	/// Records an undo command if the transform values changed during the edit session.
	/// </summary>
	private void OnTransformEditEnd()
	{
		if (_currentObject == null || EditorCommandHistory.Instance == null) return;

		var newPosition = _currentObject.LocalPosition;
		var newRotation = _currentObject.LocalRotation;
		var newScale = _currentObject.LocalScale;

		// Only record if values actually changed
		if (newPosition != _preEditPosition || newRotation != _preEditRotation || newScale != _preEditScale)
		{
			var capturedObj = _currentObject;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new TransformCommand(
					capturedObj,
					_preEditPosition, _preEditRotation, _preEditScale,
					newPosition, newRotation, newScale,
					"Transform"));
		}
	}

	private void OnInheritPositionChanged(bool inherit)
	{
		if (_currentObject == null) return;
		var old = _currentObject.InheritPosition;
		_currentObject.InheritPosition = inherit;
		RecordBoolPropertyCommand("Change Inherit Position", old, inherit,
			v => { _currentObject.InheritPosition = v; _inheritPositionCheckbox.SetPressedNoSignal(v); });
	}

	private void OnInheritRotationChanged(bool inherit)
	{
		if (_currentObject == null) return;
		var old = _currentObject.InheritRotation;
		_currentObject.InheritRotation = inherit;
		RecordBoolPropertyCommand("Change Inherit Rotation", old, inherit,
			v => { _currentObject.InheritRotation = v; _inheritRotationCheckbox.SetPressedNoSignal(v); });
	}

	private void OnInheritScaleChanged(bool inherit)
	{
		if (_currentObject == null) return;
		var old = _currentObject.InheritScale;
		_currentObject.InheritScale = inherit;
		RecordBoolPropertyCommand("Change Inherit Scale", old, inherit,
			v => { _currentObject.InheritScale = v; _inheritScaleCheckbox.SetPressedNoSignal(v); });
	}

	/// <summary>
	/// Helper that records a bool property change command only when the value actually changed.
	/// </summary>
	private void RecordBoolPropertyCommand(string description, bool oldValue, bool newValue, Action<bool> apply)
	{
		if (oldValue == newValue || EditorCommandHistory.Instance == null) return;
		EditorCommandHistory.Instance.PushWithoutExecute(
			new PropertyChangeCommand<bool>(description, oldValue, newValue, apply));
	}

	/// <summary>
	/// Hooks focus-enter/exit on a light SpinBox's internal LineEdit so we can capture
	/// the pre-edit value and record an undo command when the user finishes editing.
	/// </summary>
	private void HookLightSpinBoxUndo(SpinBox spinBox,
		Func<float> getPreEdit, Action<float> setPreEdit,
		string description, Action<float> applyValue)
	{
		spinBox.Ready += () =>
		{
			var lineEdit = spinBox.GetLineEdit();
			if (lineEdit == null) return;

			lineEdit.FocusEntered += () => setPreEdit((float)spinBox.Value);
			lineEdit.FocusExited += () =>
			{
				var pre = getPreEdit();
				var cur = (float)spinBox.Value;
				if (Math.Abs(cur - pre) < 1e-6f) return;
				if (EditorCommandHistory.Instance == null) return;
				var capturedPre = pre; var capturedCur = cur;
				EditorCommandHistory.Instance.PushWithoutExecute(
					new PropertyChangeCommand<float>(description, capturedPre, capturedCur, v =>
					{
						spinBox.SetValueNoSignal(v);
						applyValue(v);
						RefreshFromObject();
					}));
			};
		};
	}
	
	private void AutoKeyframe(string propertyPath)
	{
		if (_currentObject == null || TimelinePanel.Instance == null) return;
		
		// Add keyframe at current timeline frame
		TimelinePanel.Instance.AddKeyframeForProperty(_currentObject, propertyPath, TimelinePanel.Instance.CurrentFrame);
	}

	// ── Undo/Redo helpers ────────────────────────────────────────────────────

	/// <summary>
	/// Captures the current transform of <paramref name="obj"/> into the pre-edit
	/// fields so that the next recorded command has the correct "before" state.
	/// Called when an object is selected and after each recorded transform command.
	/// </summary>
	private void CapturePreEditTransform(SceneObject obj)
	{
		if (obj == null) return;
		if (obj is BoneSceneObject boneObj)
		{
			_preEditPosition = boneObj.TargetPosition;
			_preEditRotation = boneObj.TargetRotation;
		}
		else
		{
			_preEditPosition = obj.LocalPosition;
			_preEditRotation = obj.LocalRotation;
		}
		_preEditScale = obj.LocalScale;
	}

	/// <summary>
	/// Records a <see cref="TransformCommand"/> from the stored pre-edit state to
	/// the object's current transform, then updates the pre-edit state.
	/// Called from <see cref="OnPositionChanged"/>, <see cref="OnRotationChanged"/>,
	/// and <see cref="OnScaleChanged"/>.
	/// </summary>
	private void RecordTransformCommand(string description = "Transform")
	{
		if (_currentObject == null || _suppressUndoRecord) return;
		if (EditorCommandHistory.Instance == null) return;

		Vector3 newPos, newRot;
		if (_currentObject is BoneSceneObject boneObj)
		{
			newPos = boneObj.TargetPosition;
			newRot = boneObj.TargetRotation;
		}
		else
		{
			newPos = _currentObject.LocalPosition;
			newRot = _currentObject.LocalRotation;
		}
		var newScale = _currentObject.LocalScale;

		// Only record if something actually changed from the last recorded state
		if (newPos == _preEditPosition && newRot == _preEditRotation && newScale == _preEditScale)
			return;

		var cmd = new TransformCommand(
			_currentObject,
			_preEditPosition, _preEditRotation, _preEditScale,
			newPos, newRot, newScale,
			description);

		EditorCommandHistory.Instance.PushWithoutExecute(cmd);

		// Update pre-edit state so the next change records from the new baseline
		_preEditPosition = newPos;
		_preEditRotation = newRot;
		_preEditScale = newScale;
	}
	
	private void OnResetPosition()
	{
		if (_currentObject == null) return;
		
		// For bones, reset TargetPosition to zero (back to base pose)
		// For other objects, reset to original position
		if (_currentObject is BoneSceneObject boneObj)
		{
			boneObj.TargetPosition = Vector3.Zero;
			
			// Update UI to reflect the change (zero)
			_positionX.Value = 0;
			_positionY.Value = 0;
			_positionZ.Value = 0;
		}
		else
		{
			_currentObject.SetLocalPosition(_originalPosition);
			
			// Update UI to reflect the change
			_positionX.Value = Math.Round(_originalPosition.X * 16, 2);
			_positionY.Value = Math.Round(_originalPosition.Y * 16, 2);
			_positionZ.Value = Math.Round(_originalPosition.Z * 16, 2);
		}
		
		// Auto-keyframe when property changes
		AutoKeyframe("position.x");
		AutoKeyframe("position.y");
		AutoKeyframe("position.z");
	}
	
	private void OnResetRotation()
	{
		if (_currentObject == null) return;
		
		// For bones, reset TargetRotation to zero (back to base pose)
		// For other objects, reset to original rotation
		if (_currentObject is BoneSceneObject boneObj)
		{
			boneObj.TargetRotation = Vector3.Zero;
			
			// Update UI to reflect the change (zero)
			_rotationX.Value = 0;
			_rotationY.Value = 0;
			_rotationZ.Value = 0;
		}
		else
		{
			_currentObject.SetLocalRotation(_originalRotation);
			
			// Update UI to reflect the change
			_rotationX.Value = Math.Round(Mathf.RadToDeg(_originalRotation.X), 2);
			_rotationY.Value = Math.Round(Mathf.RadToDeg(_originalRotation.Y), 2);
			_rotationZ.Value = Math.Round(Mathf.RadToDeg(_originalRotation.Z), 2);
		}
		
		// Auto-keyframe when property changes
		AutoKeyframe("rotation.x");
		AutoKeyframe("rotation.y");
		AutoKeyframe("rotation.z");
	}
	
	private void OnResetScale()
	{
		if (_currentObject == null) return;
		
		// Reset to original scale
		_currentObject.SetLocalScale(_originalScale);
		
		// Update UI to reflect the change
		_scaleX.Value = Math.Round(_originalScale.X, 2);
		_scaleY.Value = Math.Round(_originalScale.Y, 2);
		_scaleZ.Value = Math.Round(_originalScale.Z, 2);
		
		// Auto-keyframe when property changes
		AutoKeyframe("scale.x");
		AutoKeyframe("scale.y");
		AutoKeyframe("scale.z");
	}
	
	private void OnResetPivotOffset()
	{
		if (_currentObject == null) return;
		
		// Reset to original pivot offset
		_currentObject.PivotOffset = _originalPivotOffset;
		
		// Update UI to reflect the change
		_pivotOffsetX.Value = Math.Round(_originalPivotOffset.X * 16, 2);
		_pivotOffsetY.Value = Math.Round(_originalPivotOffset.Y * 16, 2);
		_pivotOffsetZ.Value = Math.Round(_originalPivotOffset.Z * 16, 2);
		
		// Note: Pivot offset is NOT auto-keyframed as it's a non-animated property
	}

	private void OnMaterialAlphaChanged(double value)
	{
		if (_currentObject == null) return;

		var alpha = (float)value;
		_materialAlphaLabel.Text = alpha.ToString("F2");

		// Special handling for bones with alpha overrides
		if (_currentObject is BoneSceneObject boneObj)
		{
			// Check if this bone controls a single mesh
			if (boneObj.ControlsSingleMesh())
			{
				// Set the alpha override on the bone, which will apply to its controlled mesh
				boneObj.AlphaOverride = alpha;
				
				// Auto-keyframe when property changes
				AutoKeyframe("material.alpha");
				return;
			}

			// This bone is part of a skinned mesh or controls multiple meshes
			// Alpha should be controlled by the material editor, not here
			// Reset the slider to show that it's not applicable
			_materialAlphaSlider.SetValueNoSignal(1.0);
			_materialAlphaLabel.Text = "N/A";
			return;
		}

		// Ensure MaterialSettings exists and mark as explicit
		if (_currentObject.MaterialSettings == null)
		{
			_currentObject.MaterialSettings = new MaterialSettings();
		}
		_currentObject.SetExplicitMaterialSettings();
		
		// Update all materials on all surfaces of the object (for non-bone objects)
		var meshInstances = _currentObject.GetMeshInstancesRecursively(_currentObject.Visual);
		foreach (var meshInstance in meshInstances)
		{
			if (meshInstance.Mesh == null) continue;
			
			// Apply alpha to all surfaces
			for (int i = 0; i < meshInstance.Mesh.GetSurfaceCount(); i++)
			{
				var material = meshInstance.Mesh.SurfaceGetMaterial(i);
				if (material is StandardMaterial3D stdMat)
				{
					var color = stdMat.AlbedoColor;
					color.A = alpha;
					stdMat.AlbedoColor = color;
				}
			}
		}
		
		// Now update MaterialSettings after applying to meshes
		var matColor = _currentObject.MaterialSettings.AlbedoColor;
		matColor.A = alpha;
		_currentObject.MaterialSettings.AlbedoColor = matColor;

		// Propagate the updated settings to children
		_currentObject.PropagateMaterialSettingsToChildren();

		// Auto-keyframe when property changes
		AutoKeyframe("material.alpha");
	}

	private void OnResetMaterialAlpha()
	{
		if (_currentObject == null) return;
		
		// Reset to full opacity
		_materialAlphaSlider.Value = 1.0;
		
		// Auto-keyframe when property changes
		AutoKeyframe("material.alpha");
	}

	private void OnMaterialAlphaModeChanged(long index)
	{
		if (_currentObject == null) return;
		
		// Get the selected ID using GetItemId which takes the internal index
		int selectedId = (int)_materialAlphaModeDropdown.GetItemId((int)_materialAlphaModeDropdown.Selected);
		var transparencyMode = (BaseMaterial3D.TransparencyEnum)selectedId;
		
		// Ensure MaterialSettings exists - create if needed
		if (_currentObject.MaterialSettings == null)
		{
			_currentObject.MaterialSettings = new MaterialSettings();
		}
		_currentObject.SetExplicitMaterialSettings();

		// Update the MaterialSettings transparency value
		_currentObject.MaterialSettings.Transparency = transparencyMode;

		// Apply directly to current object's meshes FIRST
		var meshInstances = _currentObject.GetMeshInstancesRecursively(_currentObject.Visual);
		foreach (var meshInstance in meshInstances.Where(meshInstance => meshInstance.Mesh != null))
		{
			for (int i = 0; i < meshInstance.Mesh.GetSurfaceCount(); i++)
			{
				var material = meshInstance.Mesh.SurfaceGetMaterial(i);
				if (material is StandardMaterial3D stdMat)
				{
					stdMat.Transparency = transparencyMode;
				}
			}
		}

		// Propagate the updated settings to children
		_currentObject.PropagateMaterialSettingsToChildren();
	}

	private void OnMaterialAlbedoChanged(Color color)
	{
		ApplyMaterialProperty("albedo", color);
		AutoKeyframe("material.albedo");
	}

	private void OnMaterialMetallicChanged(double value)
	{
		var metallic = (float)value;
		_materialMetallicLabel.Text = metallic.ToString("F2");
		ApplyMaterialProperty("metallic", metallic);
		AutoKeyframe("material.metallic");
	}

	private void OnMaterialRoughnessChanged(double value)
	{
		var roughness = (float)value;
		_materialRoughnessLabel.Text = roughness.ToString("F2");
		ApplyMaterialProperty("roughness", roughness);
		AutoKeyframe("material.roughness");
	}

	private void OnMaterialNormalMapPressed()
	{
		NativeFileDialog.ShowOpenFile(
			title: "Select Normal Map",
			filters: NativeFileDialog.Filters.Images,
			callback: OnNormalMapFileSelected
		);
	}

	private void OnNormalMapFileSelected(bool success, string path)
	{
		if (!success || string.IsNullOrEmpty(path)) return;
		
		var texture = GD.Load<Texture2D>(path);
		if (texture != null)
		{
			ApplyMaterialProperty("normal", texture);
			_materialNormalMapButton.Text = System.IO.Path.GetFileName(path);
			AutoKeyframe("material.normal");
		}
	}

	private void OnClearNormalMapPressed()
	{
		ApplyMaterialProperty("normal", null);
		_materialNormalMapButton.Text = "None";
		AutoKeyframe("material.normal");
	}

	private void OnMaterialEmissionEnabledChanged(bool enabled)
	{
		ApplyMaterialProperty("emission_enabled", enabled);
		AutoKeyframe("material.emission_enabled");
	}

	private void OnMaterialEmissionColorChanged(Godot.Color color)
	{
		ApplyMaterialProperty("emission_color", color);
		AutoKeyframe("material.emission_color");
	}

	private void OnMaterialEmissionEnergyChanged(double value)
	{
		var energy = (float)value;
		_materialEmissionEnergyLabel.Text = energy.ToString("F2");
		ApplyMaterialProperty("emission_energy", energy);
		AutoKeyframe("material.emission_energy");
	}

	/// <summary>
	/// Applies a material property to all surfaces of all mesh instances on the current object.
	/// Also updates the MaterialSettings for inheritance propagation.
	/// </summary>
	private void ApplyMaterialProperty(string propertyName, object value)
	{
		switch (_currentObject)
		{
			case null:
			// Special handling for bones with alpha overrides
			// For bones in a skinned mesh, material editing is not supported through this path
			case BoneSceneObject:
				return;
		}

		// Ensure MaterialSettings exists and mark as explicit
		if (_currentObject.MaterialSettings == null)
		{
			_currentObject.MaterialSettings = new MaterialSettings();
		}
		_currentObject.SetExplicitMaterialSettings();

		var meshInstances = _currentObject.GetMeshInstancesRecursively(_currentObject.Visual);
		foreach (var meshInstance in meshInstances.Where(meshInstance => meshInstance.Mesh != null))
		{
			for (int i = 0; i < meshInstance.Mesh.GetSurfaceCount(); i++)
			{
				var material = meshInstance.Mesh.SurfaceGetMaterial(i);
				if (material is not StandardMaterial3D stdMat) continue;

				switch (propertyName)
				{
					case "albedo":
						if (value is Color color)
						{
							stdMat.AlbedoColor = color;
							_currentObject.MaterialSettings.AlbedoColor = color;
						}
						break;
					case "metallic":
						if (value is float metallic)
						{
							stdMat.Metallic = metallic;
							_currentObject.MaterialSettings.Metallic = metallic;
						}
						break;
					case "roughness":
						if (value is float roughness)
						{
							stdMat.Roughness = roughness;
							_currentObject.MaterialSettings.Roughness = roughness;
						}
						break;
					case "normal":
						if (value is Texture2D normalTex)
						{
							stdMat.NormalEnabled = true;
							stdMat.NormalTexture = normalTex;
							_currentObject.MaterialSettings.NormalEnabled = true;
							_currentObject.MaterialSettings.NormalTexture = normalTex;
						}
						else
						{
							stdMat.NormalEnabled = false;
							stdMat.NormalTexture = null;
							_currentObject.MaterialSettings.NormalEnabled = false;
							_currentObject.MaterialSettings.NormalTexture = null;
						}
						break;
					case "emission_enabled":
						if (value is bool enabled)
						{
							stdMat.EmissionEnabled = enabled;
							_currentObject.MaterialSettings.EmissionEnabled = enabled;
						}
						break;
					case "emission_color":
						if (value is Color emissionColor)
						{
							stdMat.Emission = emissionColor;
							_currentObject.MaterialSettings.EmissionColor = emissionColor;
						}
						break;
					case "emission_energy":
						if (value is float emissionEnergy)
						{
							stdMat.EmissionEnergyMultiplier = emissionEnergy;
							_currentObject.MaterialSettings.EmissionEnergy = emissionEnergy;
						}
						break;
				}
			}
		}

		// Propagate the updated settings to children
		_currentObject.PropagateMaterialSettingsToChildren();
	}

	/// <summary>
	/// Gets the current material property value from the first available material.
	/// </summary>
	private T GetMaterialProperty<T>(string propertyName)
	{
		if (_currentObject == null) return default;

		var meshInstances = _currentObject.GetMeshInstancesRecursively(_currentObject.Visual);
		if (meshInstances.Count > 0 && meshInstances[0].Mesh != null && meshInstances[0].Mesh.GetSurfaceCount() > 0)
		{
			var material = meshInstances[0].Mesh.SurfaceGetMaterial(0);
			if (material is StandardMaterial3D stdMat)
			{
				switch (propertyName)
				{
					case "albedo": return (T)(object)stdMat.AlbedoColor;
					case "metallic": return (T)(object)stdMat.Metallic;
					case "roughness": return (T)(object)stdMat.Roughness;
					case "normal": return (T)(object)stdMat.NormalTexture;
					case "emission_enabled": return (T)(object)stdMat.EmissionEnabled;
					case "emission_color": return (T)(object)stdMat.Emission;
					case "emission_energy": return (T)(object)stdMat.EmissionEnergyMultiplier;
				}
			}
		}
		return default;
	}

	// ── Light property handlers ──────────────────────────────────────────────

	private void OnLightEnergyChanged(double value)
	{
		if (_currentObject is not LightSceneObject lightObj) return;
		lightObj.LightEnergy = (float)value;
		AutoKeyframe("light.energy");
	}

	private void OnLightRangeChanged(double value)
	{
		if (_currentObject is not LightSceneObject lightObj) return;
		lightObj.LightRange = (float)value;
		AutoKeyframe("light.range");
	}

	private void OnLightIndirectEnergyChanged(double value)
	{
		if (_currentObject is not LightSceneObject lightObj) return;
		lightObj.LightIndirectEnergy = (float)value;
		AutoKeyframe("light.indirect_energy");
	}

	private void OnLightSpecularChanged(double value)
	{
		if (_currentObject is not LightSceneObject lightObj) return;
		lightObj.LightSpecular = (float)value;
		AutoKeyframe("light.specular");
	}

	private void OnLightShadowToggled(bool enabled)
	{
		if (_currentObject is not LightSceneObject lightObj) return;
		var old = lightObj.LightShadowEnabled;
		lightObj.LightShadowEnabled = enabled;

		if (old != enabled && EditorCommandHistory.Instance != null)
		{
			var capturedObj = lightObj;
			var capturedOld = old; var capturedNew = enabled;
			EditorCommandHistory.Instance.PushWithoutExecute(
				new PropertyChangeCommand<bool>(
					"Change Light Shadow",
					capturedOld, capturedNew,
					v =>
					{
						capturedObj.LightShadowEnabled = v;
						_lightShadowCheckbox.SetPressedNoSignal(v);
					}));
		}
		// Shadow is not keyframed (boolean toggle, not typically animated)
	}

	private void OnResetLightProperties()
	{
		if (_currentObject is not LightSceneObject lightObj) return;

		// Reset to defaults
		lightObj.LightColor = Colors.White;
		lightObj.LightEnergy = 1.0f;
		lightObj.LightRange = 5.0f;
		lightObj.LightIndirectEnergy = 1.0f;
		lightObj.LightSpecular = 0.5f;
		lightObj.LightShadowEnabled = true;

		// Update UI
		_lightColorPicker2.Color = Colors.White;
		_lightEnergySpinBox.Value = 1.0;
		_lightRangeSpinBox.Value = 5.0;
		_lightIndirectEnergySpinBox.Value = 1.0;
		_lightSpecularSpinBox.Value = 0.5;
		_lightShadowCheckbox.SetPressedNoSignal(true);

		// Auto-keyframe all light properties
		AutoKeyframe("light.energy");
		AutoKeyframe("light.range");
		AutoKeyframe("light.indirect_energy");
		AutoKeyframe("light.specular");
		AutoKeyframe("light.color.r");
		AutoKeyframe("light.color.g");
		AutoKeyframe("light.color.b");
	}

	// ── Bend property handlers ────────────────────────────────────────────────

	/// <summary>
	/// Called when the user starts editing a bend angle spinbox.
	/// Captures the current bend angle as the pre-edit baseline.
	/// </summary>
	private void OnBendEditBegin()
	{
		if (_currentObject is not BoneSceneObject boneObj) return;
		if (!boneObj.BendParameters.HasValue) return;
		_preEditBendAngle = boneObj.BendParameters.Value.Angle;
	}

	/// <summary>
	/// Called when the user finishes editing a bend angle spinbox.
	/// Records an undo command if the angle changed.
	/// </summary>
	private void OnBendEditEnd()
	{
		if (_currentObject is not BoneSceneObject boneObj) return;
		if (!boneObj.BendParameters.HasValue) return;
		if (EditorCommandHistory.Instance == null) return;

		var newAngle = boneObj.BendParameters.Value.Angle;
		if (newAngle == _preEditBendAngle) return;

		var capturedObj = boneObj;
		var capturedPre = _preEditBendAngle;
		var capturedNew = newAngle;

		EditorCommandHistory.Instance.PushWithoutExecute(
			new PropertyChangeCommand<Godot.Vector3>(
				"Change Bend Angle",
				capturedPre, capturedNew,
				v =>
				{
					capturedObj.SetBendAngle(v);
					if (Instance != null && SelectionManager.Instance != null &&
						SelectionManager.Instance.SelectedObjects.Contains(capturedObj))
						Instance.RefreshFromObject();
				}));

		_preEditBendAngle = newAngle;
	}

	/// <summary>
	/// Called whenever a bend angle spinbox value changes.
	/// Applies the new angle to the bone and regenerates its meshes in real time.
	/// </summary>
	private void OnBendAngleChanged()
	{
		if (_currentObject is not BoneSceneObject boneObj) return;
		if (!boneObj.BendParameters.HasValue) return;

		var newAngle = new Vector3(
			(float)_bendAngleX.Value,
			(float)_bendAngleY.Value,
			(float)_bendAngleZ.Value
		);

		boneObj.SetBendAngle(newAngle);

		// Auto-keyframe bend angle
		AutoKeyframe("bend.angle.x");
		AutoKeyframe("bend.angle.y");
		AutoKeyframe("bend.angle.z");
	}

	/// <summary>
	/// Resets the bend angle to zero (straight/undeformed).
	/// </summary>
	private void OnResetBendAngle()
	{
		if (_currentObject is not BoneSceneObject boneObj) return;
		if (!boneObj.BendParameters.HasValue) return;

		boneObj.SetBendAngle(Vector3.Zero);

		_bendAngleX.SetValueNoSignal(0);
		_bendAngleY.SetValueNoSignal(0);
		_bendAngleZ.SetValueNoSignal(0);

		AutoKeyframe("bend.angle.x");
		AutoKeyframe("bend.angle.y");
		AutoKeyframe("bend.angle.z");
	}
}

/// <summary>
/// A collapsible section with a toggle arrow that shows/hides all content at once
/// </summary>
public partial class CollapsibleSection : VBoxContainer
{
	private Button _toggleButton;
	private Button _resetButton;
	private VBoxContainer _contentContainer;
	private bool _isExpanded = true;

	public CollapsibleSection(string title)
	{
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		// Header with toggle arrow
		var header = new HBoxContainer();
		AddChild(header);

        // Toggle arrow button
        _toggleButton = new Button
        {
            Text = "▼",
            CustomMinimumSize = new Vector2(24, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin
        };
        _toggleButton.Pressed += OnTogglePressed;
		header.AddChild(_toggleButton);

        var label = new Label
        {
            Text = title
        };
        label.AddThemeFontSizeOverride("font_size", 14);
		label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(label);

        // Reset button
        _resetButton = new Button
        {
            Text = "↺",
            TooltipText = "Reset to original value",
            CustomMinimumSize = new Vector2(24, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd
        };
        header.AddChild(_resetButton);

		// Content container for the spinbox rows
		_contentContainer = new VBoxContainer();
		AddChild(_contentContainer);
	}

	private void OnTogglePressed()
	{
		_isExpanded = !_isExpanded;
		_contentContainer.Visible = _isExpanded;
		_toggleButton.Text = _isExpanded ? "▼" : "▶";
	}

	public VBoxContainer GetContentContainer() => _contentContainer;
	
	public Button GetResetButton() => _resetButton;
}
