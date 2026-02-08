using Godot;
using System;

namespace simplyRemadeNuxi.core;

public partial class ObjectPropertiesPanel : Panel
{
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
	private SceneObject _currentObject;
	
	// Store original values for reset functionality
	private Vector3 _originalPosition = Vector3.Zero;
	private Vector3 _originalRotation = Vector3.Zero;
	private Vector3 _originalScale = Vector3.One;
	private Vector3 _originalPivotOffset = new Vector3(0, -0.5f, 0);

	public override void _Ready()
	{
		SetupUi();
		SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
	}

	public override void _ExitTree()
	{
		if (SelectionManager.Instance != null)
		{
			SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
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

		// Position section with toggle arrow
		_positionSection = new CollapsibleSection("Position");
		_positionSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_positionSection);
		_positionSection.GetResetButton().Pressed += OnResetPosition;
		
		var posContainer = _positionSection.GetContentContainer();
		_positionX = CreateSpinBoxRow(posContainer, "X:", OnPositionChanged);
		_positionY = CreateSpinBoxRow(posContainer, "Y:", OnPositionChanged);
		_positionZ = CreateSpinBoxRow(posContainer, "Z:", OnPositionChanged);

		// Rotation section with toggle arrow
		_rotationSection = new CollapsibleSection("Rotation (degrees)");
		_rotationSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_rotationSection);
		_rotationSection.GetResetButton().Pressed += OnResetRotation;
		
		var rotContainer = _rotationSection.GetContentContainer();
		_rotationX = CreateSpinBoxRow(rotContainer, "X:", OnRotationChanged);
		_rotationY = CreateSpinBoxRow(rotContainer, "Y:", OnRotationChanged);
		_rotationZ = CreateSpinBoxRow(rotContainer, "Z:", OnRotationChanged);

		// Scale section with toggle arrow
		_scaleSection = new CollapsibleSection("Scale");
		_scaleSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_scaleSection);
		_scaleSection.GetResetButton().Pressed += OnResetScale;
		
		var scaleContainer = _scaleSection.GetContentContainer();
		_scaleX = CreateSpinBoxRow(scaleContainer, "X:", OnScaleChanged);
		_scaleY = CreateSpinBoxRow(scaleContainer, "Y:", OnScaleChanged);
		_scaleZ = CreateSpinBoxRow(scaleContainer, "Z:", OnScaleChanged);

		// Pivot Offset section with toggle arrow (non-animated property)
		_pivotOffsetSection = new CollapsibleSection("Pivot Offset");
		_pivotOffsetSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_pivotOffsetSection);
		_pivotOffsetSection.GetResetButton().Pressed += OnResetPivotOffset;
		
		var pivotOffsetContainer = _pivotOffsetSection.GetContentContainer();
		_pivotOffsetX = CreateSpinBoxRow(pivotOffsetContainer, "X:", OnPivotOffsetChanged);
		_pivotOffsetY = CreateSpinBoxRow(pivotOffsetContainer, "Y:", OnPivotOffsetChanged);
		_pivotOffsetZ = CreateSpinBoxRow(pivotOffsetContainer, "Z:", OnPivotOffsetChanged);
	}

	private SpinBox CreateSpinBoxRow(VBoxContainer parent, string labelText, Action onChanged)
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
		row.AddChild(spin);

		return spin;
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
			ClearSpinBoxes();
		}
	}

	private void UpdateUiFromObject()
	{
		if (_currentObject == null) return;

		_objectNameLabel.Text = _currentObject.Name;

		// Visibility
		_visibilityCheckbox.SetPressedNoSignal(_currentObject.ObjectVisible);

		// For bones, show TargetPosition and TargetRotation (offset from base pose)
		// For other objects, show regular Position and Rotation
		Vector3 pos, rot;
		if (_currentObject is BoneSceneObject boneObj)
		{
			pos = boneObj.TargetPosition;
			rot = boneObj.TargetRotation;
		}
		else
		{
			pos = _currentObject.Position;
			rot = _currentObject.Rotation;
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
		var scale = _currentObject.Scale;
		_scaleX.SetValueNoSignal(Math.Round(scale.X, 2));
		_scaleY.SetValueNoSignal(Math.Round(scale.Y, 2));
		_scaleZ.SetValueNoSignal(Math.Round(scale.Z, 2));

		// Pivot Offset (scaled by 16 for display, same as position)
		var pivotOffset = _currentObject.PivotOffset;
		_pivotOffsetX.SetValueNoSignal(Math.Round(pivotOffset.X * 16, 2));
		_pivotOffsetY.SetValueNoSignal(Math.Round(pivotOffset.Y * 16, 2));
		_pivotOffsetZ.SetValueNoSignal(Math.Round(pivotOffset.Z * 16, 2));
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
	}

	private void OnVisibilityChanged(bool visible)
	{
		if (_currentObject == null) return;

		_currentObject.SetObjectVisible(visible);
		
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
			_currentObject.Position = newPos;
		}
		
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
			_currentObject.Rotation = newRot;
		}
		
		// Auto-keyframe when property changes
		AutoKeyframe("rotation.x");
		AutoKeyframe("rotation.y");
		AutoKeyframe("rotation.z");
	}

	private void OnScaleChanged()
	{
		if (_currentObject == null) return;

		_currentObject.Scale = new Vector3(
			(float)_scaleX.Value,
			(float)_scaleY.Value,
			(float)_scaleZ.Value
		);
		
		// Auto-keyframe when property changes
		AutoKeyframe("scale.x");
		AutoKeyframe("scale.y");
		AutoKeyframe("scale.z");
	}

	private void OnPivotOffsetChanged()
	{
		if (_currentObject == null) return;

		_currentObject.PivotOffset = new Vector3(
			(float)_pivotOffsetX.Value / 16,
			(float)_pivotOffsetY.Value / 16,
			(float)_pivotOffsetZ.Value / 16
		);
		
		// Note: Pivot offset is NOT auto-keyframed as it's a non-animated property
	}
	
	private void AutoKeyframe(string propertyPath)
	{
		if (_currentObject == null || TimelinePanel.Instance == null) return;
		
		// Add keyframe at current timeline frame
		TimelinePanel.Instance.AddKeyframeForProperty(_currentObject, propertyPath, TimelinePanel.Instance.CurrentFrame);
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
			_currentObject.Position = _originalPosition;
			
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
			_currentObject.Rotation = _originalRotation;
			
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
		_currentObject.Scale = _originalScale;
		
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

	public CollapsibleSection() : this("") { }

	public CollapsibleSection(string title)
	{
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		// Header with toggle arrow
		var header = new HBoxContainer();
		AddChild(header);

		// Toggle arrow button
		_toggleButton = new Button();
		_toggleButton.Text = "▼";
		_toggleButton.CustomMinimumSize = new Vector2(24, 0);
		_toggleButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		_toggleButton.Pressed += OnTogglePressed;
		header.AddChild(_toggleButton);

		var label = new Label();
		label.Text = title;
		label.AddThemeFontSizeOverride("font_size", 14);
		label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(label);

		// Reset button
		_resetButton = new Button();
		_resetButton.Text = "↺";
		_resetButton.TooltipText = "Reset to original value";
		_resetButton.CustomMinimumSize = new Vector2(24, 0);
		_resetButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
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
