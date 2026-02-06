using Godot;
using System;
using System.Collections.Generic;

namespace simplyRemadeNuxi.core;

public partial class TimelinePanel : Panel
{
	private HSplitContainer _splitContainer;
	private ScrollContainer _propertiesScroll;
	private VBoxContainer _propertiesContainer;
	private ScrollContainer _keyframesScroll;
	private VBoxContainer _keyframesTracksContainer;
	private Control _playheadContainer;
	private ColorRect _playhead;
	private Label _timeLabel;
	private ScrollContainer _frameRulerScroll;
	private Control _frameRuler;
	private Button _playPauseButton;
	
	// Timeline settings (frame-based)
	private int _currentFrame = 0;
	private int _maxFrames = 300; // Default 300 frames (10 seconds at 30fps)
	private float _frameRate = 30f; // 30 fps default
	private float _pixelsPerFrame = 5f; // Width per frame in pixels
	private bool _isDraggingPlayhead = false;
	private bool _isPlaying = false;
	private int _playStartFrame = 0; // Frame when play was pressed
	
	// Property tracking
	private List<AnimatableProperty> _properties = new List<AnimatableProperty>();
	
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

	public override void _Process(double delta)
	{
		if (_isPlaying)
		{
			// Advance playhead when playing
			_currentFrame++;
			if (_currentFrame > _maxFrames)
			{
				_currentFrame = 0; // Loop back to start
			}
		}
		
		// Manually sync scroll positions every frame as backup
		if (_keyframesScroll != null && _frameRulerScroll != null)
		{
			if (_frameRulerScroll.ScrollHorizontal != _keyframesScroll.ScrollHorizontal)
			{
				_frameRulerScroll.ScrollHorizontal = _keyframesScroll.ScrollHorizontal;
			}
		}
		
		UpdatePlayheadPosition();
	}

	private void SetupUi()
	{
		// Main split container (2-column layout)
		_splitContainer = new HSplitContainer();
		_splitContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(_splitContainer);

		// Left column - Property names
		SetupPropertiesColumn();

		// Right column - Keyframes and timeline
		SetupKeyframesColumn();
	}

	private void SetupPropertiesColumn()
	{
		var leftContainer = new VBoxContainer();
		leftContainer.CustomMinimumSize = new Vector2(150, 0); // Set initial width
		leftContainer.SizeFlagsHorizontal = SizeFlags.Fill; // Don't expand, just fill to min size
		leftContainer.SizeFlagsStretchRatio = 0.2f; // Take 20% of space
		_splitContainer.AddChild(leftContainer);

		// Header for properties (matches height of transport controls on right)
		var headerLabel = new Label();
		headerLabel.Text = "Properties";
		headerLabel.AddThemeFontSizeOverride("font_size", 14);
		headerLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
		headerLabel.CustomMinimumSize = new Vector2(0, 30);
		headerLabel.VerticalAlignment = VerticalAlignment.Center;
		leftContainer.AddChild(headerLabel);

		// Spacer to match frame ruler height on right side
		var rulerSpacer = new Control();
		rulerSpacer.CustomMinimumSize = new Vector2(0, 25);
		leftContainer.AddChild(rulerSpacer);

		// Separator
		var separator = new HSeparator();
		leftContainer.AddChild(separator);

		// Scrollable properties list
		_propertiesScroll = new ScrollContainer();
		_propertiesScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_propertiesScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_propertiesScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		leftContainer.AddChild(_propertiesScroll);

		_propertiesContainer = new VBoxContainer();
		_propertiesContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_propertiesScroll.AddChild(_propertiesContainer);
	}

	private void SetupKeyframesColumn()
	{
		var rightContainer = new VBoxContainer();
		rightContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_splitContainer.AddChild(rightContainer);

		// Timeline header with info label and transport controls
		var timelineHeaderBar = new HBoxContainer();
		timelineHeaderBar.CustomMinimumSize = new Vector2(0, 30);
		timelineHeaderBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightContainer.AddChild(timelineHeaderBar);

		// Transport controls
		AddTransportControls(timelineHeaderBar);

		// Frame label (shows current frame)
		_timeLabel = new Label();
		_timeLabel.Text = "Frame: 0";
		_timeLabel.VerticalAlignment = VerticalAlignment.Center;
		_timeLabel.CustomMinimumSize = new Vector2(120, 0);
		_timeLabel.HorizontalAlignment = HorizontalAlignment.Right;
		timelineHeaderBar.AddChild(_timeLabel);

		// Frame ruler container with clipping
		var frameRulerContainer = new Container();
		frameRulerContainer.CustomMinimumSize = new Vector2(0, 25);
		frameRulerContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		frameRulerContainer.ClipContents = true; // Clip child content to bounds
		rightContainer.AddChild(frameRulerContainer);
		
		// Frame ruler scroll (inside clipping container)
		_frameRulerScroll = new ScrollContainer();
		_frameRulerScroll.SetAnchorsPreset(LayoutPreset.FullRect);
		_frameRulerScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_frameRulerScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto; // Allow programmatic scrolling
		frameRulerContainer.AddChild(_frameRulerScroll);

		_frameRuler = new Control();
		_frameRuler.CustomMinimumSize = new Vector2(_maxFrames * _pixelsPerFrame, 25);
		_frameRuler.Size = new Vector2(_maxFrames * _pixelsPerFrame, 25);
		_frameRulerScroll.AddChild(_frameRuler);

		// Draw frame markers on ruler
		_frameRuler.Draw += DrawFrameRuler;

		// Separator
		var separator = new HSeparator();
		rightContainer.AddChild(separator);

		// Keyframes area with playhead (overlaid structure)
		var keyframesAndPlayheadContainer = new Control();
		keyframesAndPlayheadContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		keyframesAndPlayheadContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		keyframesAndPlayheadContainer.ClipContents = false;
		rightContainer.AddChild(keyframesAndPlayheadContainer);

		// Keyframes scroll (base layer)
		_keyframesScroll = new ScrollContainer();
		_keyframesScroll.SetAnchorsPreset(LayoutPreset.FullRect);
		_keyframesScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto; // Enable vertical scrolling
		keyframesAndPlayheadContainer.AddChild(_keyframesScroll);

		// Container for keyframe tracks
		_keyframesTracksContainer = new VBoxContainer();
		_keyframesTracksContainer.CustomMinimumSize = new Vector2(_maxFrames * _pixelsPerFrame, 0);
		_keyframesTracksContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_keyframesScroll.AddChild(_keyframesTracksContainer);

		// Playhead container (overlays on top)
		_playheadContainer = new Control();
		_playheadContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		_playheadContainer.MouseFilter = MouseFilterEnum.Ignore; // Don't block mouse events to scrollbar
		keyframesAndPlayheadContainer.AddChild(_playheadContainer);

		// Playhead visual
		_playhead = new ColorRect();
		_playhead.Color = new Color(1, 0.3f, 0.3f, 0.8f);
		_playhead.Size = new Vector2(2, 300);
		_playhead.MouseFilter = MouseFilterEnum.Stop;
		_playheadContainer.AddChild(_playhead);

		// Playhead handle (top)
		var playheadHandle = new ColorRect();
		playheadHandle.Color = new Color(1, 0.3f, 0.3f);
		playheadHandle.Size = new Vector2(12, 12);
		playheadHandle.Position = new Vector2(-5, -6);
		playheadHandle.MouseFilter = MouseFilterEnum.Stop;
		_playhead.AddChild(playheadHandle);

		// Setup playhead dragging
		playheadHandle.GuiInput += OnPlayheadInput;

		// Timeline scrubbing via click on tracks
		_keyframesTracksContainer.GuiInput += OnTimelineInput;

		// Sync scrolling between properties and keyframes
		_propertiesScroll.GetVScrollBar().ValueChanged += (value) =>
		{
			_keyframesScroll.ScrollVertical = (int)value;
		};
		_keyframesScroll.GetVScrollBar().ValueChanged += (value) =>
		{
			_propertiesScroll.ScrollVertical = (int)value;
		};

		// Sync horizontal scrolling between frame ruler and keyframes (bidirectional)
		_keyframesScroll.GetHScrollBar().ValueChanged += (value) =>
		{
			if (_frameRulerScroll.ScrollHorizontal != (int)value)
			{
				_frameRulerScroll.ScrollHorizontal = (int)value;
			}
		};
		_frameRulerScroll.GetHScrollBar().ValueChanged += (value) =>
		{
			if (_keyframesScroll.ScrollHorizontal != (int)value)
			{
				_keyframesScroll.ScrollHorizontal = (int)value;
			}
		};
	}

	private void AddTransportControls(HBoxContainer parent)
	{
		// Jump to start button - use vaadin
		var jumpStartButton = new Button();
		var jumpStartIcon = GD.Load<Texture2D>("res://assets/img/icon/vaadin--step-backward.svg");
		jumpStartButton.Icon = jumpStartIcon;
		jumpStartButton.TooltipText = "Jump to Start";
		jumpStartButton.CustomMinimumSize = new Vector2(28, 28);
		jumpStartButton.Flat = true;
		jumpStartButton.Pressed += OnJumpToStart;
		parent.AddChild(jumpStartButton);

		// Step backward button - use mdi
		var stepBackButton = new Button();
		var stepBackIcon = GD.Load<Texture2D>("res://assets/img/icon/mdi--step-backward.svg");
		stepBackButton.Icon = stepBackIcon;
		stepBackButton.TooltipText = "Step Backward";
		stepBackButton.CustomMinimumSize = new Vector2(28, 28);
		stepBackButton.Flat = true;
		stepBackButton.Pressed += OnStepBackward;
		parent.AddChild(stepBackButton);

		// Stop button
		var stopButton = new Button();
		var stopIcon = GD.Load<Texture2D>("res://assets/img/icon/material-symbols--stop.svg");
		stopButton.Icon = stopIcon;
		stopButton.TooltipText = "Stop";
		stopButton.CustomMinimumSize = new Vector2(28, 28);
		stopButton.Flat = true;
		stopButton.Pressed += OnStop;
		parent.AddChild(stopButton);

		// Play/Pause button
		_playPauseButton = new Button();
		var playIcon = GD.Load<Texture2D>("res://assets/img/icon/mdi--play.svg");
		_playPauseButton.Icon = playIcon;
		_playPauseButton.TooltipText = "Play";
		_playPauseButton.CustomMinimumSize = new Vector2(28, 28);
		_playPauseButton.Flat = true;
		_playPauseButton.Pressed += OnPlayPause;
		parent.AddChild(_playPauseButton);

		// Step forward button - use mdi
		var stepForwardButton = new Button();
		var stepForwardIcon = GD.Load<Texture2D>("res://assets/img/icon/mdi--step-forward.svg");
		stepForwardButton.Icon = stepForwardIcon;
		stepForwardButton.TooltipText = "Step Forward";
		stepForwardButton.CustomMinimumSize = new Vector2(28, 28);
		stepForwardButton.Flat = true;
		stepForwardButton.Pressed += OnStepForward;
		parent.AddChild(stepForwardButton);

		// Jump to end button - use vaadin
		var jumpEndButton = new Button();
		var jumpEndIcon = GD.Load<Texture2D>("res://assets/img/icon/vaadin--step-forward.svg");
		jumpEndButton.Icon = jumpEndIcon;
		jumpEndButton.TooltipText = "Jump to End";
		jumpEndButton.CustomMinimumSize = new Vector2(28, 28);
		jumpEndButton.Flat = true;
		jumpEndButton.Pressed += OnJumpToEnd;
		parent.AddChild(jumpEndButton);

		// Spacer
		var spacer = new Control();
		spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(spacer);
	}

	private void OnJumpToStart()
	{
		_currentFrame = 0;
		_isPlaying = false;
		UpdatePlayPauseButton();
	}

	private void OnStepBackward()
	{
		_currentFrame = Mathf.Max(0, _currentFrame - 1);
		_isPlaying = false;
		UpdatePlayPauseButton();
	}

	private void OnStop()
	{
		_currentFrame = _playStartFrame; // Return to frame when play was pressed
		_isPlaying = false;
		UpdatePlayPauseButton();
	}

	private void OnPlayPause()
	{
		if (!_isPlaying)
		{
			// Store current frame when starting playback
			_playStartFrame = _currentFrame;
		}
		_isPlaying = !_isPlaying;
		UpdatePlayPauseButton();
	}

	private void OnStepForward()
	{
		_currentFrame = Mathf.Min(_maxFrames, _currentFrame + 1);
		_isPlaying = false;
		UpdatePlayPauseButton();
	}

	private void OnJumpToEnd()
	{
		_currentFrame = _maxFrames;
		_isPlaying = false;
		UpdatePlayPauseButton();
	}

	private void UpdatePlayPauseButton()
	{
		if (_playPauseButton != null)
		{
			if (_isPlaying)
			{
				var pauseIcon = GD.Load<Texture2D>("res://assets/img/icon/material-symbols--pause.svg");
				_playPauseButton.Icon = pauseIcon;
				_playPauseButton.TooltipText = "Pause";
			}
			else
			{
				var playIcon = GD.Load<Texture2D>("res://assets/img/icon/mdi--play.svg");
				_playPauseButton.Icon = playIcon;
				_playPauseButton.TooltipText = "Play";
			}
		}
	}

	private void DrawFrameRuler()
	{
		float rulerWidth = _frameRuler.Size.X;
		if (rulerWidth <= 0) rulerWidth = _maxFrames * _pixelsPerFrame;
		
		// Draw frame numbers at the top of the timeline
		for (int frame = 0; frame <= _maxFrames; frame += 10)
		{
			float x = frame * _pixelsPerFrame;
			
			// Skip if out of bounds
			if (x > rulerWidth) break;
			
			// Draw tick mark
			_frameRuler.DrawLine(
				new Vector2(x, _frameRuler.Size.Y - 5),
				new Vector2(x, _frameRuler.Size.Y),
				new Color(0.6f, 0.6f, 0.6f),
				2.0f
			);
			
			// Draw frame number
			var font = ThemeDB.FallbackFont;
			var fontSize = 11;
			var textColor = new Color(0.85f, 0.85f, 0.85f);
			var text = frame.ToString();
			
			// Calculate text size for centering
			var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
			var textPos = new Vector2(x - textSize.X / 2, _frameRuler.Size.Y - 8);
			
			// Only draw if the entire text fits within bounds (with small margin)
			if (textPos.X >= 0 && (textPos.X + textSize.X) <= rulerWidth)
			{
				_frameRuler.DrawString(font, textPos, text, HorizontalAlignment.Left, -1, fontSize, textColor);
			}
		}
		
		// Draw minor ticks every 5 frames
		for (int frame = 5; frame <= _maxFrames; frame += 5)
		{
			if (frame % 10 == 0) continue; // Skip major ticks
			
			float x = frame * _pixelsPerFrame;
			
			// Skip if out of bounds
			if (x > rulerWidth) break;
			
			_frameRuler.DrawLine(
				new Vector2(x, _frameRuler.Size.Y - 3),
				new Vector2(x, _frameRuler.Size.Y),
				new Color(0.4f, 0.4f, 0.4f),
				1.0f
			);
		}
	}

	private void OnPlayheadInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				_isDraggingPlayhead = mouseButton.Pressed;
			}
		}
	}

	private void OnTimelineInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
			{
				// Click to move playhead
				float localX = mouseButton.Position.X;
				_currentFrame = Mathf.Clamp(Mathf.RoundToInt(localX / _pixelsPerFrame), 0, _maxFrames);
				UpdatePlayheadPosition();
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (_isDraggingPlayhead && @event is InputEventMouseMotion mouseMotion)
		{
			// Get mouse position relative to keyframes container
			var globalPos = mouseMotion.GlobalPosition;
			var localPos = _keyframesTracksContainer.GlobalPosition;
			float localX = globalPos.X - localPos.X + _keyframesScroll.ScrollHorizontal;
			
			_currentFrame = Mathf.Clamp(Mathf.RoundToInt(localX / _pixelsPerFrame), 0, _maxFrames);
			UpdatePlayheadPosition();
		}

		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
			{
				_isDraggingPlayhead = false;
			}
		}
	}

	private void UpdatePlayheadPosition()
	{
		if (_playhead == null) return;
		
		// Calculate playhead position in content space (absolute position)
		float xPos = _currentFrame * _pixelsPerFrame;
		_playhead.Position = new Vector2(xPos, 0);
		
		// Calculate time from frame and framerate
		float timeInSeconds = _currentFrame / _frameRate;
		_timeLabel.Text = $"Frame: {_currentFrame} ({timeInSeconds:F2}s)";
		
		// Update playhead height to match content
		if (_keyframesTracksContainer != null)
		{
			_playhead.Size = new Vector2(2, Mathf.Max(_keyframesTracksContainer.Size.Y, 300));
		}
	}

	private void OnSelectionChanged()
	{
		RefreshProperties();
	}

	private void RefreshProperties()
	{
		// Clear existing properties and tracks
		foreach (var child in _propertiesContainer.GetChildren())
		{
			child.QueueFree();
		}
		foreach (var child in _keyframesTracksContainer.GetChildren())
		{
			child.QueueFree();
		}
		_properties.Clear();

		var selectedObjects = SelectionManager.Instance.SelectedObjects;
		if (selectedObjects.Count == 0)
		{
			var noSelectionLabel = new Label();
			noSelectionLabel.Text = "No object selected";
			noSelectionLabel.HorizontalAlignment = HorizontalAlignment.Center;
			noSelectionLabel.VerticalAlignment = VerticalAlignment.Center;
			_propertiesContainer.AddChild(noSelectionLabel);
			
			var noSelectionRightLabel = new Label();
			noSelectionRightLabel.Text = "";
			noSelectionRightLabel.CustomMinimumSize = new Vector2(0, 30);
			_keyframesTracksContainer.AddChild(noSelectionRightLabel);
			return;
		}

		// Add properties for selected objects
		foreach (var obj in selectedObjects)
		{
			AddObjectProperties(obj);
		}
	}

	private void AddObjectProperties(SceneObject obj)
	{
		// Object header (left)
		var objectHeaderLeft = new Control();
		objectHeaderLeft.CustomMinimumSize = new Vector2(0, 25);
		objectHeaderLeft.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_propertiesContainer.AddChild(objectHeaderLeft);
		
		var objectLabel = new Label();
		objectLabel.Text = obj.Name;
		objectLabel.AddThemeFontSizeOverride("font_size", 12);
		objectLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.5f));
		objectLabel.VerticalAlignment = VerticalAlignment.Center;
		objectLabel.SetAnchorsPreset(LayoutPreset.FullRect);
		objectHeaderLeft.AddChild(objectLabel);

		// Object header track (right) - empty spacer that matches height
		var objectHeaderRight = new Control();
		objectHeaderRight.CustomMinimumSize = new Vector2(0, 25);
		objectHeaderRight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_keyframesTracksContainer.AddChild(objectHeaderRight);

		// Position properties
		AddCollapsiblePropertyGroup(obj, "Position", new string[] { "position.x", "position.y", "position.z" });

		// Rotation properties
		AddCollapsiblePropertyGroup(obj, "Rotation", new string[] { "rotation.x", "rotation.y", "rotation.z" });

		// Scale properties
		AddCollapsiblePropertyGroup(obj, "Scale", new string[] { "scale.x", "scale.y", "scale.z" });
	}

	private void AddCollapsiblePropertyGroup(SceneObject obj, string groupName, string[] propertyPaths)
	{
		// Create the left property group
		var propertyGroup = new CollapsiblePropertyGroup(groupName, propertyPaths, obj);
		_propertiesContainer.AddChild(propertyGroup);
		
		// Create the right keyframe track group
		var trackGroup = new KeyframeTrackGroup(_maxFrames, _pixelsPerFrame, propertyPaths.Length);
		_keyframesTracksContainer.AddChild(trackGroup);
		
		// Link them together so they expand/collapse in sync
		propertyGroup.PropertyExpanded += (expanded) =>
		{
			trackGroup.SetExpanded(expanded);
		};
		
		// Store property references
		_properties.Add(new AnimatableProperty 
		{ 
			Object = obj, 
			PropertyPath = groupName.ToLower(), 
			PropertyGroup = propertyGroup,
			TrackGroup = trackGroup
		});
	}
}

/// <summary>
/// Represents a collapsible property group in the timeline (e.g., Position with x, y, z)
/// </summary>
public partial class CollapsiblePropertyGroup : VBoxContainer
{
	[Signal] public delegate void PropertyExpandedEventHandler(bool expanded);
	
	private Button _toggleButton;
	private VBoxContainer _childPropertiesContainer;
	private bool _isExpanded = false;
	private string _groupName;
	private string[] _propertyPaths;
	private SceneObject _object;

	public CollapsiblePropertyGroup(string groupName, string[] propertyPaths, SceneObject obj)
	{
		_groupName = groupName;
		_propertyPaths = propertyPaths;
		_object = obj;

		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		// Header with toggle button
		var header = new HBoxContainer();
		header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		AddChild(header);

		// Toggle arrow button
		_toggleButton = new Button();
		_toggleButton.Text = "▶";
		_toggleButton.CustomMinimumSize = new Vector2(20, 20);
		_toggleButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		_toggleButton.Flat = true; // Remove button background
		// Remove all padding from button
		_toggleButton.AddThemeConstantOverride("h_separation", 0);
		_toggleButton.Pressed += OnTogglePressed;
		header.AddChild(_toggleButton);

		// Property name label
		var label = new Label();
		label.Text = groupName;
		label.VerticalAlignment = VerticalAlignment.Center;
		header.AddChild(label);

		// Add keyframe button
		var addKeyButton = new Button();
		addKeyButton.Text = "+";
		addKeyButton.CustomMinimumSize = new Vector2(24, 20);
		addKeyButton.TooltipText = "Add Keyframe";
		addKeyButton.Flat = true; // Remove button background
		addKeyButton.Pressed += () => AddKeyframe(_groupName.ToLower());
		header.AddChild(addKeyButton);

		// Child properties container (x, y, z)
		_childPropertiesContainer = new VBoxContainer();
		_childPropertiesContainer.Visible = false;
		AddChild(_childPropertiesContainer);

		// Add individual property rows
		foreach (var propPath in propertyPaths)
		{
			var propRow = CreatePropertyRow(propPath);
			_childPropertiesContainer.AddChild(propRow);
		}
	}

	private HBoxContainer CreatePropertyRow(string propertyPath)
	{
		var row = new HBoxContainer();
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		// Indent
		var indent = new Control();
		indent.CustomMinimumSize = new Vector2(20, 0);
		row.AddChild(indent);

		// Property name (e.g., "x", "y", "z")
		var propName = propertyPath.Split('.')[^1]; // Get last part after dot
		var label = new Label();
		label.Text = propName;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.CustomMinimumSize = new Vector2(40, 0);
		row.AddChild(label);

		// Add keyframe button for individual property
		var addKeyButton = new Button();
		addKeyButton.Text = "+";
		addKeyButton.CustomMinimumSize = new Vector2(24, 20);
		addKeyButton.TooltipText = $"Add Keyframe for {propName}";
		addKeyButton.Flat = true; // Remove button background
		addKeyButton.Pressed += () => AddKeyframe(propertyPath);
		row.AddChild(addKeyButton);

		return row;
	}

	private void OnTogglePressed()
	{
		_isExpanded = !_isExpanded;
		_childPropertiesContainer.Visible = _isExpanded;
		_toggleButton.Text = _isExpanded ? "▼" : "▶";
		EmitSignal(SignalName.PropertyExpanded, _isExpanded);
	}

	private void AddKeyframe(string propertyPath)
	{
		GD.Print($"Adding keyframe for {_object.Name}.{propertyPath}");
		// TODO: Implement keyframe creation logic
	}
}

/// <summary>
/// Represents a keyframe track group on the right side that matches a property group on the left
/// </summary>
public partial class KeyframeTrackGroup : VBoxContainer
{
	private Control _mainTrack;
	private VBoxContainer _childTracks;
	private int _maxFrames;
	private float _pixelsPerFrame;
	private int _childCount;

	public KeyframeTrackGroup(int maxFrames, float pixelsPerFrame, int childCount)
	{
		_maxFrames = maxFrames;
		_pixelsPerFrame = pixelsPerFrame;
		_childCount = childCount;
		
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		// Main track (collapsed state) - match left side HBoxContainer natural height
		_mainTrack = new Control();
		_mainTrack.CustomMinimumSize = new Vector2(maxFrames * pixelsPerFrame, 31); // Match button height
		_mainTrack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		AddChild(_mainTrack);

		// Draw grid background
		_mainTrack.Draw += () => DrawTrackBackground(_mainTrack, false);

		// Child tracks (expanded state)
		_childTracks = new VBoxContainer();
		_childTracks.Visible = false;
		AddChild(_childTracks);

		// Create child track rows - match left side HBoxContainer natural height
		for (int i = 0; i < childCount; i++)
		{
			var childTrack = new Control();
			childTrack.CustomMinimumSize = new Vector2(maxFrames * pixelsPerFrame, 31); // Match button height
			childTrack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_childTracks.AddChild(childTrack);
			
			// Draw grid background for child
			childTrack.Draw += () => DrawTrackBackground(childTrack, true);
		}
	}

	private void DrawTrackBackground(Control track, bool isChild)
	{
		// Draw alternating background
		var bgColor = isChild ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.12f, 0.12f, 0.12f);
		track.DrawRect(new Rect2(Vector2.Zero, track.Size), bgColor, true);

		// Draw vertical grid lines every 10 frames
		for (int frame = 0; frame <= _maxFrames; frame += 10)
		{
			float x = frame * _pixelsPerFrame;
			track.DrawLine(
				new Vector2(x, 0),
				new Vector2(x, track.Size.Y),
				new Color(0.25f, 0.25f, 0.25f, 0.5f),
				1.0f
			);
		}
	}

	public void SetExpanded(bool expanded)
	{
		_childTracks.Visible = expanded;
	}
}

/// <summary>
/// Represents an animatable property in the timeline
/// </summary>
public class AnimatableProperty
{
	public SceneObject Object { get; set; }
	public string PropertyPath { get; set; }
	public CollapsiblePropertyGroup PropertyGroup { get; set; }
	public KeyframeTrackGroup TrackGroup { get; set; }
	public List<Keyframe> Keyframes { get; set; } = new List<Keyframe>();
}

/// <summary>
/// Represents a keyframe in the timeline
/// </summary>
public class Keyframe
{
	public int Frame { get; set; }
	public object Value { get; set; }
	public string InterpolationType { get; set; } = "linear";
}
