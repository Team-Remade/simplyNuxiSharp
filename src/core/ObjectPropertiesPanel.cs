using Godot;
using System;

namespace simplyRemadeNuxi.core;

public partial class ObjectPropertiesPanel : Panel
{
	private VBoxContainer _vboxContainer;
	private Label _objectNameLabel;
	private SpinBox _positionX;
	private SpinBox _positionY;
	private SpinBox _positionZ;
	private SpinBox _rotationX;
	private SpinBox _rotationY;
	private SpinBox _rotationZ;
	private SpinBox _scaleX;
	private SpinBox _scaleY;
	private SpinBox _scaleZ;
	private CollapsibleSection _positionSection;
	private CollapsibleSection _rotationSection;
	private CollapsibleSection _scaleSection;
	private SceneObject _currentObject;

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
		var vbox = new VBoxContainer();
		vbox.Name = "VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 4);
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(vbox);
		_vboxContainer = vbox;

		// Object name label
		_objectNameLabel = new Label();
		_objectNameLabel.Name = "ObjectNameLabel";
		_objectNameLabel.Text = "No object selected";
		_objectNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_objectNameLabel);

		// Position section with toggle arrow
		_positionSection = new CollapsibleSection("Position");
		_positionSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_positionSection);
		
		var posContainer = _positionSection.GetContentContainer();
		_positionX = CreateSpinBoxRow(posContainer, "X:", OnPositionChanged);
		_positionY = CreateSpinBoxRow(posContainer, "Y:", OnPositionChanged);
		_positionZ = CreateSpinBoxRow(posContainer, "Z:", OnPositionChanged);

		// Rotation section with toggle arrow
		_rotationSection = new CollapsibleSection("Rotation (degrees)");
		_rotationSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_rotationSection);
		
		var rotContainer = _rotationSection.GetContentContainer();
		_rotationX = CreateSpinBoxRow(rotContainer, "X:", OnRotationChanged);
		_rotationY = CreateSpinBoxRow(rotContainer, "Y:", OnRotationChanged);
		_rotationZ = CreateSpinBoxRow(rotContainer, "Z:", OnRotationChanged);

		// Scale section with toggle arrow
		_scaleSection = new CollapsibleSection("Scale");
		_scaleSection.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(_scaleSection);
		
		var scaleContainer = _scaleSection.GetContentContainer();
		_scaleX = CreateSpinBoxRow(scaleContainer, "X:", OnScaleChanged);
		_scaleY = CreateSpinBoxRow(scaleContainer, "Y:", OnScaleChanged);
		_scaleZ = CreateSpinBoxRow(scaleContainer, "Z:", OnScaleChanged);
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

		// Position (scaled by 16 for display)
		var pos = _currentObject.Position;
		_positionX.Value = Math.Round(pos.X * 16, 2);
		_positionY.Value = Math.Round(pos.Y * 16, 2);
		_positionZ.Value = Math.Round(pos.Z * 16, 2);

		// Rotation (convert from radians to degrees)
		var rot = _currentObject.Rotation;
		_rotationX.Value = Math.Round(Mathf.RadToDeg(rot.X), 2);
		_rotationY.Value = Math.Round(Mathf.RadToDeg(rot.Y), 2);
		_rotationZ.Value = Math.Round(Mathf.RadToDeg(rot.Z), 2);

		// Scale
		var scale = _currentObject.Scale;
		_scaleX.Value = Math.Round(scale.X, 2);
		_scaleY.Value = Math.Round(scale.Y, 2);
		_scaleZ.Value = Math.Round(scale.Z, 2);
	}

	private void ClearSpinBoxes()
	{
		_positionX.Value = 0;
		_positionY.Value = 0;
		_positionZ.Value = 0;
		_rotationX.Value = 0;
		_rotationY.Value = 0;
		_rotationZ.Value = 0;
		_scaleX.Value = 1;
		_scaleY.Value = 1;
		_scaleZ.Value = 1;
	}

	private void OnPositionChanged()
	{
		if (_currentObject == null) return;

		_currentObject.Position = new Vector3(
			(float)_positionX.Value / 16,
			(float)_positionY.Value / 16,
			(float)_positionZ.Value / 16
		);
		
		// Auto-keyframe when property changes
		AutoKeyframe("position.x");
		AutoKeyframe("position.y");
		AutoKeyframe("position.z");
	}

	private void OnRotationChanged()
	{
		if (_currentObject == null) return;

		_currentObject.Rotation = new Vector3(
			Mathf.DegToRad((float)_rotationX.Value),
			Mathf.DegToRad((float)_rotationY.Value),
			Mathf.DegToRad((float)_rotationZ.Value)
		);
		
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
	
	private void AutoKeyframe(string propertyPath)
	{
		if (_currentObject == null || TimelinePanel.Instance == null) return;
		
		// Add keyframe at current timeline frame
		TimelinePanel.Instance.AddKeyframeForProperty(_currentObject, propertyPath, TimelinePanel.Instance.CurrentFrame);
	}
}

/// <summary>
/// A collapsible section with a toggle arrow that shows/hides all content at once
/// </summary>
public partial class CollapsibleSection : VBoxContainer
{
	private Button _toggleButton;
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
		header.AddChild(label);

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
}
