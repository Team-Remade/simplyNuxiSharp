using Godot;
using System;
using System.Collections.Generic;

namespace simplyRemadeNuxi.core;

public partial class TimelinePanel : Panel
{
	// Singleton for easy access from other panels
	private static TimelinePanel _instance;
	public static TimelinePanel Instance => _instance;
	
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
	
	// Public properties for external access
	public int CurrentFrame => _currentFrame;
	
	// Property tracking
	private List<AnimatableProperty> _properties = new List<AnimatableProperty>();
	
	// Keyframe tracking by property path
	private Dictionary<string, List<Keyframe>> _propertyKeyframes = new Dictionary<string, List<Keyframe>>();
	
	// Track all single property tracks for updating
	private List<Control> _singlePropertyTracks = new List<Control>();
	
	public override void _Ready()
	{
		_instance = this;
		SetupUi();
		SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
	}

	public override void _ExitTree()
	{
		if (SelectionManager.Instance != null)
		{
			SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
		}
		_instance = null;
	}

	public override void _Process(double delta)
	{
		int previousFrame = _currentFrame;
		
		if (_isPlaying)
		{
			// Advance playhead when playing
			_currentFrame++;
			// Find the furthest keyframe to determine loop point
			int furthestKeyframe = _maxFrames;
			foreach (var kvp in _propertyKeyframes)
			{
				foreach (var keyframe in kvp.Value)
				{
					if (keyframe.Frame > furthestKeyframe)
					{
						furthestKeyframe = keyframe.Frame;
					}
				}
			}
			if (_currentFrame > furthestKeyframe)
			{
				_currentFrame = 0; // Loop back to start after furthest keyframe
			}
			
			// Update animated textures when playing
			if (AnimatedTextureManager.Instance != null)
			{
				AnimatedTextureManager.Instance.Play();
			}
		}
		else
		{
			// Stop animated textures when not playing
			if (AnimatedTextureManager.Instance != null)
			{
				AnimatedTextureManager.Instance.Pause();
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
		
		// Manually sync vertical scroll as well
		if (_propertiesScroll != null && _keyframesScroll != null)
		{
			if (_propertiesScroll.ScrollVertical != _keyframesScroll.ScrollVertical)
			{
				_keyframesScroll.ScrollVertical = _propertiesScroll.ScrollVertical;
			}
			else if (_keyframesScroll.ScrollVertical != _propertiesScroll.ScrollVertical)
			{
				_propertiesScroll.ScrollVertical = _keyframesScroll.ScrollVertical;
			}
		}
		
		UpdatePlayheadPosition();
		
		// Apply keyframe values if frame changed
		if (previousFrame != _currentFrame)
		{
			ApplyKeyframesAtCurrentFrame();
			
			// Update animated textures time based on current frame
			if (AnimatedTextureManager.Instance != null)
			{
				float timeInSeconds = _currentFrame / _frameRate;
				AnimatedTextureManager.Instance.SetAnimationTime(timeInSeconds);
			}
		}
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
		// Add extra scroll space beyond max frames (50% more) to allow placing keyframes beyond
		var scrollableWidth = _maxFrames * _pixelsPerFrame * 1.5f;
		_frameRuler.CustomMinimumSize = new Vector2(scrollableWidth, 25);
		_frameRuler.Size = new Vector2(scrollableWidth, 25);
		_frameRulerScroll.AddChild(_frameRuler);

		// Draw frame markers on ruler
		_frameRuler.Draw += DrawFrameRuler;

		// Separator
		var separator = new HSeparator();
		rightContainer.AddChild(separator);

		// Use a container to hold both scroll and playhead overlay
		var scrollAndPlayheadContainer = new Control();
		scrollAndPlayheadContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollAndPlayheadContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollAndPlayheadContainer.ClipContents = true; // Clip playhead when it scrolls out
		rightContainer.AddChild(scrollAndPlayheadContainer);

		// Keyframes scroll (base layer)
		_keyframesScroll = new ScrollContainer();
		_keyframesScroll.SetAnchorsPreset(LayoutPreset.FullRect);
		_keyframesScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto; // Enable vertical scrolling
		_keyframesScroll.ClipContents = true;
		scrollAndPlayheadContainer.AddChild(_keyframesScroll);

		// Container for keyframe tracks
		_keyframesTracksContainer = new VBoxContainer();
		_keyframesTracksContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_keyframesTracksContainer.SizeFlagsVertical = SizeFlags.ShrinkBegin; // Shrink to content
		// Add extra scroll space beyond max frames (50% more) to allow placing keyframes beyond
		var tracksScrollableWidth = _maxFrames * _pixelsPerFrame * 1.5f;
		_keyframesTracksContainer.CustomMinimumSize = new Vector2(tracksScrollableWidth, 0);
		_keyframesScroll.AddChild(_keyframesTracksContainer);

		// Playhead container overlay (on top of scroll, positioned to align with scroll content)
		_playheadContainer = new Control();
		_playheadContainer.SetAnchorsPreset(LayoutPreset.TopLeft);
		_playheadContainer.MouseFilter = MouseFilterEnum.Ignore; // Clicks pass through to scroll
		_playheadContainer.Position = new Vector2(0, 0);
		_playheadContainer.Size = new Vector2(10000, 10000); // Large size
		scrollAndPlayheadContainer.AddChild(_playheadContainer);

		// Playhead visual (on top of tracks)
		_playhead = new ColorRect();
		_playhead.Color = new Color(1, 0.3f, 0.3f, 0.8f);
		_playhead.Size = new Vector2(2, 300);
		_playhead.MouseFilter = MouseFilterEnum.Stop;
		_playhead.ZIndex = 100; // Ensure it's on top
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
		bool _syncingScroll = false;
		_propertiesScroll.GetVScrollBar().ValueChanged += (value) =>
		{
			if (!_syncingScroll)
			{
				_syncingScroll = true;
				_keyframesScroll.ScrollVertical = (int)value;
				_syncingScroll = false;
			}
		};
		_keyframesScroll.GetVScrollBar().ValueChanged += (value) =>
		{
			if (!_syncingScroll)
			{
				_syncingScroll = true;
				_propertiesScroll.ScrollVertical = (int)value;
				_syncingScroll = false;
			}
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
		ApplyKeyframesAtCurrentFrame();
		
		// Stop animated textures
		if (AnimatedTextureManager.Instance != null)
		{
			AnimatedTextureManager.Instance.Pause();
		}
	}

	private void OnStepBackward()
	{
		_currentFrame = Mathf.Max(0, _currentFrame - 1);
		_isPlaying = false;
		UpdatePlayPauseButton();
		ApplyKeyframesAtCurrentFrame();
		
		// Stop animated textures when stepping
		if (AnimatedTextureManager.Instance != null)
		{
			AnimatedTextureManager.Instance.Pause();
		}
	}

	private void OnStop()
	{
		_currentFrame = _playStartFrame; // Return to frame when play was pressed
		_isPlaying = false;
		UpdatePlayPauseButton();
		ApplyKeyframesAtCurrentFrame();
		
		// Stop and reset animated textures
		if (AnimatedTextureManager.Instance != null)
		{
			AnimatedTextureManager.Instance.Stop();
			AnimatedTextureManager.Instance.Reset();
		}
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
		
		// Control animated textures playback
		if (AnimatedTextureManager.Instance != null)
		{
			if (_isPlaying)
			{
				AnimatedTextureManager.Instance.Play();
			}
			else
			{
				AnimatedTextureManager.Instance.Pause();
			}
		}
	}

	private void OnStepForward()
	{
		_currentFrame++; // No limit, allow going past max frames
		_isPlaying = false;
		UpdatePlayPauseButton();
		ApplyKeyframesAtCurrentFrame();
		
		// Stop animated textures when stepping
		if (AnimatedTextureManager.Instance != null)
		{
			AnimatedTextureManager.Instance.Pause();
		}
	}

	private void OnJumpToEnd()
	{
		// Jump to the furthest keyframe, or maxFrames if no keyframes beyond it
		int furthestKeyframe = _maxFrames;
		foreach (var kvp in _propertyKeyframes)
		{
			foreach (var keyframe in kvp.Value)
			{
				if (keyframe.Frame > furthestKeyframe)
				{
					furthestKeyframe = keyframe.Frame;
				}
			}
		}
		_currentFrame = furthestKeyframe;
		_isPlaying = false;
		UpdatePlayPauseButton();
		ApplyKeyframesAtCurrentFrame();
		
		// Stop animated textures
		if (AnimatedTextureManager.Instance != null)
		{
			AnimatedTextureManager.Instance.Pause();
		}
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
				// Click to move playhead - account for horizontal scroll
				float localX = mouseButton.Position.X + _keyframesScroll.ScrollHorizontal;
				int newFrame = Mathf.Max(0, Mathf.RoundToInt(localX / _pixelsPerFrame));
				
				if (newFrame != _currentFrame)
				{
					_currentFrame = newFrame;
					UpdatePlayheadPosition();
					ApplyKeyframesAtCurrentFrame(); // Apply animation when clicking timeline
				}
				
				// Don't start animated textures on click, only during playback/scrub
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (_isDraggingPlayhead && @event is InputEventMouseMotion mouseMotion)
		{
			// Get mouse position relative to keyframes container
			// The scroll is already accounted for in the position, so don't add it again
			var globalPos = mouseMotion.GlobalPosition;
			var localPos = _keyframesScroll.GlobalPosition;
			float localX = globalPos.X - localPos.X + _keyframesScroll.ScrollHorizontal;
			
			int newFrame = Mathf.Max(0, Mathf.RoundToInt(localX / _pixelsPerFrame));
			
			if (newFrame != _currentFrame)
			{
				_currentFrame = newFrame;
				UpdatePlayheadPosition();
				ApplyKeyframesAtCurrentFrame(); // Apply animation when dragging playhead
				
				// Update animated textures time when scrubbing
				if (AnimatedTextureManager.Instance != null)
				{
					float timeInSeconds = _currentFrame / _frameRate;
					AnimatedTextureManager.Instance.SetAnimationTime(timeInSeconds);
				}
			}
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
		
		// Calculate playhead position
		// X position: account for horizontal scroll and scrollbar offset
		var scrollPos = _keyframesScroll.GlobalPosition - _playheadContainer.GlobalPosition;
		float xPos = scrollPos.X + (_currentFrame * _pixelsPerFrame) - _keyframesScroll.ScrollHorizontal;
		// Y position: keep fixed at top of scroll container (don't move with vertical scroll)
		float yPos = scrollPos.Y;
		_playhead.Position = new Vector2(xPos, yPos);
		
		// Calculate time from frame and framerate
		float timeInSeconds = _currentFrame / _frameRate;
		_timeLabel.Text = $"Frame: {_currentFrame} ({timeInSeconds:F2}s)";
		
		// Update playhead height to match visible area + content
		if (_keyframesTracksContainer != null && _keyframesScroll != null)
		{
			float contentHeight = Mathf.Max(_keyframesTracksContainer.Size.Y, _keyframesScroll.Size.Y);
			_playhead.Size = new Vector2(2, contentHeight);
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
		_singlePropertyTracks.Clear();
		// Don't clear _propertyKeyframes - keep it for now but load from objects instead
		_propertyKeyframes.Clear();

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

		// Visibility property (non-collapsible single property)
		AddSingleProperty(obj, "Visible", "visible");

		// Position properties
		AddCollapsiblePropertyGroup(obj, "Position", new string[] { "position.x", "position.y", "position.z" });

		// Rotation properties
		AddCollapsiblePropertyGroup(obj, "Rotation", new string[] { "rotation.x", "rotation.y", "rotation.z" });

		// Scale properties
		AddCollapsiblePropertyGroup(obj, "Scale", new string[] { "scale.x", "scale.y", "scale.z" });
	}

	private void AddSingleProperty(SceneObject obj, string propertyName, string propertyPath)
	{
		// Create the left property row
		var propertyRow = new HBoxContainer();
		propertyRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_propertiesContainer.AddChild(propertyRow);
		
		// Property name label
		var label = new Label();
		label.Text = propertyName;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.CustomMinimumSize = new Vector2(60, 0);
		propertyRow.AddChild(label);
		
		// Add keyframe button
		var addKeyButton = new Button();
		addKeyButton.Text = "+";
		addKeyButton.CustomMinimumSize = new Vector2(24, 20);
		addKeyButton.TooltipText = $"Add Keyframe for {propertyName}";
		addKeyButton.Flat = true;
		addKeyButton.Pressed += () => AddKeyframeForProperty(obj, propertyPath, CurrentFrame);
		propertyRow.AddChild(addKeyButton);
		
		// Create the right keyframe track (single track, no collapsing)
		var track = new Control();
		// Add extra scroll space beyond max frames (50% more) to allow placing keyframes beyond
		var trackScrollableWidth = _maxFrames * _pixelsPerFrame * 1.5f;
		track.CustomMinimumSize = new Vector2(trackScrollableWidth, 31);
		track.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_keyframesTracksContainer.AddChild(track);
		_singlePropertyTracks.Add(track); // Store reference for updates
		
		// Draw grid background and keyframes
		track.Draw += () =>
		{
			// Draw alternating background
			var bgColor = new Color(0.12f, 0.12f, 0.12f);
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
			
			// Draw keyframes
			var keyframes = GetKeyframesForProperty(obj, propertyPath);
			foreach (var keyframe in keyframes)
			{
				float x = keyframe.Frame * _pixelsPerFrame;
				float y = track.Size.Y / 2;
				float size = 6f;
				
				// Draw diamond shape
				var points = new Vector2[]
				{
					new Vector2(x, y - size),
					new Vector2(x + size, y),
					new Vector2(x, y + size),
					new Vector2(x - size, y)
				};
				
				track.DrawColoredPolygon(points, new Color(1f, 0.8f, 0.2f));
				
				for (int i = 0; i < points.Length; i++)
				{
					var nextI = (i + 1) % points.Length;
					track.DrawLine(points[i], points[nextI], new Color(0.8f, 0.6f, 0.1f), 1.5f);
				}
			}
		};
		
		// Handle track input for adding/removing/dragging keyframes
		bool isDragging = false;
		Keyframe draggedKeyframe = null;
		int dragStartFrame = 0;
		
		track.GuiInput += (inputEvent) =>
		{
			if (inputEvent is InputEventMouseButton mouseButton)
			{
				if (mouseButton.ButtonIndex == MouseButton.Left)
				{
					float localX = mouseButton.Position.X;
					int frame = Mathf.RoundToInt(localX / _pixelsPerFrame);
					frame = Mathf.Max(0, frame); // Only clamp to 0, allow going past max
					
					if (mouseButton.Pressed)
					{
						var keyframes = GetKeyframesForProperty(obj, propertyPath);
						var clickedKeyframe = keyframes.Find(k => Mathf.Abs(k.Frame - frame) <= 1);
						
						if (clickedKeyframe != null && mouseButton.AltPressed)
						{
							RemoveKeyframeForProperty(obj, propertyPath, clickedKeyframe.Frame);
						}
						else if (clickedKeyframe != null)
						{
							isDragging = true;
							draggedKeyframe = clickedKeyframe;
							dragStartFrame = clickedKeyframe.Frame;
						}
						else
						{
							AddKeyframeForProperty(obj, propertyPath, frame);
						}
					}
					else
					{
						if (isDragging && draggedKeyframe != null)
						{
							if (draggedKeyframe.Frame != dragStartFrame)
							{
								MoveKeyframe(obj, propertyPath, dragStartFrame, draggedKeyframe.Frame);
							}
							isDragging = false;
							draggedKeyframe = null;
						}
					}
				}
			}
			else if (inputEvent is InputEventMouseMotion mouseMotion && isDragging && draggedKeyframe != null)
			{
				float localX = mouseMotion.Position.X;
				int newFrame = Mathf.RoundToInt(localX / _pixelsPerFrame);
				newFrame = Mathf.Max(0, newFrame); // Only clamp to 0, allow going past max
				
				if (newFrame != draggedKeyframe.Frame)
				{
					draggedKeyframe.Frame = newFrame;
					track.QueueRedraw();
				}
			}
		};
		
		// Store property reference and load keyframes from object
		var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
		
		// Only initialize if not already present - LoadKeyframesFromObject will create the list if needed
		if (!_propertyKeyframes.ContainsKey(fullPath))
		{
			_propertyKeyframes[fullPath] = new List<Keyframe>();
		}
		
		// Load keyframes from SceneObject (this will replace the empty list if keyframes exist)
		LoadKeyframesFromObject(obj, propertyPath);
		
		_properties.Add(new AnimatableProperty
		{
			Object = obj,
			PropertyPath = propertyPath,
			PropertyGroup = null,
			TrackGroup = null
		});
	}

	private void AddCollapsiblePropertyGroup(SceneObject obj, string groupName, string[] propertyPaths)
	{
		// Create the left property group
		var propertyGroup = new CollapsiblePropertyGroup(groupName, propertyPaths, obj, this);
		_propertiesContainer.AddChild(propertyGroup);
		
		// Create the right keyframe track group
		var trackGroup = new KeyframeTrackGroup(_maxFrames, _pixelsPerFrame, propertyPaths.Length, obj, propertyPaths, this);
		_keyframesTracksContainer.AddChild(trackGroup);
		
		// Link them together so they expand/collapse in sync
		propertyGroup.PropertyExpanded += (expanded) =>
		{
			trackGroup.SetExpanded(expanded);
		};
		
		// Store property references and load keyframes from object
		foreach (var propPath in propertyPaths)
		{
			var fullPath = $"{obj.GetInstanceId()}.{propPath}";
			
			// Only initialize if not already present - LoadKeyframesFromObject will create the list if needed
			if (!_propertyKeyframes.ContainsKey(fullPath))
			{
				_propertyKeyframes[fullPath] = new List<Keyframe>();
			}
			
			// Load keyframes from SceneObject (this will replace the empty list if keyframes exist)
			LoadKeyframesFromObject(obj, propPath);
		}
		
		_properties.Add(new AnimatableProperty
		{
			Object = obj,
			PropertyPath = groupName.ToLower(),
			PropertyGroup = propertyGroup,
			TrackGroup = trackGroup
		});
	}
	
	/// <summary>
	/// Load keyframes from the SceneObject into the timeline's working dictionary
	/// </summary>
	private void LoadKeyframesFromObject(SceneObject obj, string propertyPath)
	{
		if (obj.Keyframes.ContainsKey(propertyPath) && obj.Keyframes[propertyPath].Count > 0)
		{
			var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
			
			// Create Timeline Keyframe objects from ObjectKeyframe objects
			_propertyKeyframes[fullPath] = new List<Keyframe>();
			foreach (var objKeyframe in obj.Keyframes[propertyPath])
			{
				_propertyKeyframes[fullPath].Add(new Keyframe
				{
					Frame = objKeyframe.Frame,
					Value = objKeyframe.Value,
					InterpolationType = objKeyframe.InterpolationType
				});
			}
			_propertyKeyframes[fullPath].Sort((a, b) => a.Frame.CompareTo(b.Frame));
			
			// Check if any keyframe extends past current max
			RecalculateTimelineLength();
		}
	}
	
	/// <summary>
	/// Recalculate timeline length based on the furthest keyframe
	/// </summary>
	private void RecalculateTimelineLength()
	{
		int maxKeyframeFrame = 300; // Default minimum
		
		foreach (var kvp in _propertyKeyframes)
		{
			foreach (var keyframe in kvp.Value)
			{
				if (keyframe.Frame > maxKeyframeFrame)
				{
					maxKeyframeFrame = keyframe.Frame;
				}
			}
		}
		
		if (maxKeyframeFrame > _maxFrames)
		{
			ExtendTimeline(maxKeyframeFrame);
		}
	}
	
	/// <summary>
	/// Save keyframes from the timeline's working dictionary back to the SceneObject
	/// </summary>
	private void SaveKeyframesToObject(SceneObject obj, string propertyPath)
	{
		var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
		
		if (!_propertyKeyframes.ContainsKey(fullPath) || _propertyKeyframes[fullPath].Count == 0)
		{
			// No keyframes in timeline, ensure object has none either
			if (obj.Keyframes.ContainsKey(propertyPath))
			{
				obj.Keyframes.Remove(propertyPath);
			}
			return;
		}
		
		// Create ObjectKeyframe objects from Timeline Keyframe objects
		if (!obj.Keyframes.ContainsKey(propertyPath))
		{
			obj.Keyframes[propertyPath] = new List<ObjectKeyframe>();
		}
		
		obj.Keyframes[propertyPath].Clear();
		foreach (var keyframe in _propertyKeyframes[fullPath])
		{
			obj.Keyframes[propertyPath].Add(new ObjectKeyframe
			{
				Frame = keyframe.Frame,
				Value = keyframe.Value,
				InterpolationType = keyframe.InterpolationType
			});
		}
	}
	
	public void AddKeyframeForProperty(SceneObject obj, string propertyPath, int frame)
	{
		var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
		
		// Get current value from object
		var value = GetPropertyValue(obj, propertyPath);
		
		// Check if keyframe already exists at this frame
		if (!_propertyKeyframes.ContainsKey(fullPath))
		{
			_propertyKeyframes[fullPath] = new List<Keyframe>();
		}
		
		var existingKeyframe = _propertyKeyframes[fullPath].Find(k => k.Frame == frame);
		if (existingKeyframe != null)
		{
			// Update existing keyframe value
			existingKeyframe.Value = value;
		}
		else
		{
			// Add new keyframe
			var keyframe = new Keyframe
			{
				Frame = frame,
				Value = value,
				InterpolationType = "linear"
			};
			_propertyKeyframes[fullPath].Add(keyframe);
			_propertyKeyframes[fullPath].Sort((a, b) => a.Frame.CompareTo(b.Frame));
		}
		
		// Extend timeline if keyframe is past the current max
		if (frame > _maxFrames)
		{
			ExtendTimeline(frame);
		}
		
		// Save to SceneObject
		SaveKeyframesToObject(obj, propertyPath);
		
		// Refresh the track to show the new keyframe
		RefreshTracks();
	}
	
	public void RemoveKeyframeForProperty(SceneObject obj, string propertyPath, int frame)
	{
		var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
		
		if (_propertyKeyframes.ContainsKey(fullPath))
		{
			var keyframe = _propertyKeyframes[fullPath].Find(k => k.Frame == frame);
			if (keyframe != null)
			{
				_propertyKeyframes[fullPath].Remove(keyframe);
				
				// Save to SceneObject
				SaveKeyframesToObject(obj, propertyPath);
				
				RefreshTracks();
			}
		}
	}
	
	public void MoveKeyframe(SceneObject obj, string propertyPath, int fromFrame, int toFrame)
	{
		var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
		
		if (_propertyKeyframes.ContainsKey(fullPath))
		{
			var keyframe = _propertyKeyframes[fullPath].Find(k => k.Frame == fromFrame);
			if (keyframe != null)
			{
				// Check if there's already a keyframe at the target frame
				var existingKeyframe = _propertyKeyframes[fullPath].Find(k => k.Frame == toFrame && k != keyframe);
				if (existingKeyframe != null)
				{
					// Replace the existing keyframe
					_propertyKeyframes[fullPath].Remove(existingKeyframe);
				}
				
				// Update frame and re-sort
				keyframe.Frame = toFrame;
				_propertyKeyframes[fullPath].Sort((a, b) => a.Frame.CompareTo(b.Frame));
				
				// Extend timeline if keyframe is past the current max
				if (toFrame > _maxFrames)
				{
					ExtendTimeline(toFrame);
				}
				
				// Save to SceneObject
				SaveKeyframesToObject(obj, propertyPath);
				
				RefreshTracks();
			}
		}
	}
	
	public List<Keyframe> GetKeyframesForProperty(SceneObject obj, string propertyPath)
	{
		var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
		
		if (_propertyKeyframes.ContainsKey(fullPath))
		{
			return _propertyKeyframes[fullPath];
		}
		
		return new List<Keyframe>();
	}
	
	private object GetPropertyValue(SceneObject obj, string propertyPath)
	{
		// Handle single property (like "visible")
		if (propertyPath == "visible")
		{
			return obj.ObjectVisible ? 1f : 0f;
		}
		
		var parts = propertyPath.Split('.');
		
		if (parts.Length == 2)
		{
			var propName = parts[0];
			var component = parts[1];
			
			switch (propName)
			{
				case "position":
					return component switch
					{
						"x" => obj.Position.X,
						"y" => obj.Position.Y,
						"z" => obj.Position.Z,
						_ => 0f
					};
				case "rotation":
					return component switch
					{
						"x" => obj.RotationDegrees.X,
						"y" => obj.RotationDegrees.Y,
						"z" => obj.RotationDegrees.Z,
						_ => 0f
					};
				case "scale":
					return component switch
					{
						"x" => obj.Scale.X,
						"y" => obj.Scale.Y,
						"z" => obj.Scale.Z,
						_ => 1f
					};
			}
		}
		
		return 0f;
	}
	
	private void ExtendTimeline(int newMaxFrame)
	{
		// Update max frames to accommodate the new keyframe
		_maxFrames = newMaxFrame;
		
		// Add extra scroll space beyond max frames (50% more) to allow placing keyframes beyond
		var scrollableWidth = _maxFrames * _pixelsPerFrame * 1.5f;
		
		// Update frame ruler size
		if (_frameRuler != null)
		{
			_frameRuler.CustomMinimumSize = new Vector2(scrollableWidth, 25);
			_frameRuler.Size = new Vector2(scrollableWidth, 25);
			_frameRuler.QueueRedraw();
		}
		
		// Update keyframe tracks container size
		if (_keyframesTracksContainer != null)
		{
			_keyframesTracksContainer.CustomMinimumSize = new Vector2(scrollableWidth, 0);
		}
		
		// Update all single property track controls
		foreach (var track in _singlePropertyTracks)
		{
			if (track != null)
			{
				track.CustomMinimumSize = new Vector2(scrollableWidth, track.CustomMinimumSize.Y);
				track.QueueRedraw();
			}
		}
		
		// Update all track group controls to new size
		foreach (var prop in _properties)
		{
			if (prop.TrackGroup != null)
			{
				prop.TrackGroup.UpdateMaxFrames(_maxFrames);
			}
		}
	}
	
	private void RefreshTracks()
	{
		// Queue draw on all tracks to update keyframe visualization
		foreach (var prop in _properties)
		{
			if (prop.TrackGroup != null)
			{
				prop.TrackGroup.QueueRedrawTracks();
			}
		}
	}
	
	private void ApplyKeyframesAtCurrentFrame()
	{
		// Don't apply keyframes while user is dragging the gizmo
		if (SelectionManager.Instance != null && SelectionManager.Instance.IsGizmoEditing)
		{
			return;
		}
		
		// Apply keyframe values for all animated properties at current frame
		foreach (var kvp in _propertyKeyframes)
		{
			var fullPath = kvp.Key;
			var keyframes = kvp.Value;
			
			if (keyframes.Count == 0) continue;
			
			// Parse the full path to get object ID and property path
			var pathParts = fullPath.Split('.');
			if (pathParts.Length < 2) continue;
			
			var objectIdStr = pathParts[0];
			string propertyPath;
			
			// Handle single property (like "visible") vs compound property (like "position.x")
			if (pathParts.Length == 2)
			{
				propertyPath = pathParts[1]; // e.g., "visible"
			}
			else // pathParts.Length >= 3
			{
				var propertyType = pathParts[1];
				var component = pathParts[2];
				propertyPath = $"{propertyType}.{component}"; // e.g., "position.x"
			}
			
			// Find the object
			if (!ulong.TryParse(objectIdStr, out ulong objectId)) continue;
			
			SceneObject targetObject = null;
			foreach (var prop in _properties)
			{
				if (prop.Object.GetInstanceId() == objectId)
				{
					targetObject = prop.Object;
					break;
				}
			}
			
			if (targetObject == null) continue;
			
			// Find keyframes around current frame
			Keyframe prevKeyframe = null;
			Keyframe nextKeyframe = null;
			
			foreach (var kf in keyframes)
			{
				if (kf.Frame <= _currentFrame)
				{
					if (prevKeyframe == null || kf.Frame > prevKeyframe.Frame)
					{
						prevKeyframe = kf;
					}
				}
				if (kf.Frame >= _currentFrame)
				{
					if (nextKeyframe == null || kf.Frame < nextKeyframe.Frame)
					{
						nextKeyframe = kf;
					}
				}
			}
			
			// Calculate interpolated or step value
			float value = 0f;
			
			// For "visible" property, use step interpolation (no smoothing)
			if (propertyPath == "visible")
			{
				if (prevKeyframe != null)
				{
					value = Convert.ToSingle(prevKeyframe.Value);
				}
			}
			else
			{
				// For other properties, use linear interpolation
				if (prevKeyframe != null && nextKeyframe != null && prevKeyframe.Frame != nextKeyframe.Frame)
				{
					// Interpolate between keyframes
					float t = (_currentFrame - prevKeyframe.Frame) / (float)(nextKeyframe.Frame - prevKeyframe.Frame);
					float prevValue = Convert.ToSingle(prevKeyframe.Value);
					float nextValue = Convert.ToSingle(nextKeyframe.Value);
					value = Mathf.Lerp(prevValue, nextValue, t);
				}
				else if (prevKeyframe != null)
				{
					// Use exact keyframe value
					value = Convert.ToSingle(prevKeyframe.Value);
				}
			}
			
			// Apply value to object
			SetPropertyValue(targetObject, propertyPath, value);
		}
	}
	
	private void SetPropertyValue(SceneObject obj, string propertyPath, float value)
	{
		// Handle single property (like "visible")
		if (propertyPath == "visible")
		{
			obj.SetObjectVisible(value >= 0.5f);
			return;
		}
		
		var parts = propertyPath.Split('.');
		
		if (parts.Length == 2)
		{
			var propName = parts[0];
			var component = parts[1];
			
			switch (propName)
			{
				case "position":
					var pos = obj.Position;
					switch (component)
					{
						case "x": pos.X = value; break;
						case "y": pos.Y = value; break;
						case "z": pos.Z = value; break;
					}
					obj.Position = pos;
					break;
					
				case "rotation":
					var rot = obj.RotationDegrees;
					switch (component)
					{
						case "x": rot.X = value; break;
						case "y": rot.Y = value; break;
						case "z": rot.Z = value; break;
					}
					obj.RotationDegrees = rot;
					break;
					
				case "scale":
					var scale = obj.Scale;
					switch (component)
					{
						case "x": scale.X = value; break;
						case "y": scale.Y = value; break;
						case "z": scale.Z = value; break;
					}
					obj.Scale = scale;
					break;
			}
		}
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
	private TimelinePanel _timeline;

	public CollapsiblePropertyGroup(string groupName, string[] propertyPaths, SceneObject obj, TimelinePanel timeline)
	{
		_groupName = groupName;
		_propertyPaths = propertyPaths;
		_object = obj;
		_timeline = timeline;

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
		if (_timeline != null)
		{
			// Check if this is a group keyframe (position, rotation, scale) or individual property
			if (propertyPath == _groupName.ToLower())
			{
				// Add keyframes for all child properties
				foreach (var childPath in _propertyPaths)
				{
					_timeline.AddKeyframeForProperty(_object, childPath, _timeline.CurrentFrame);
				}
			}
			else
			{
				// Add keyframe for individual property
				_timeline.AddKeyframeForProperty(_object, propertyPath, _timeline.CurrentFrame);
			}
		}
	}
}

/// <summary>
/// Represents a keyframe track group on the right side that matches a property group on the left
/// </summary>
public partial class KeyframeTrackGroup : VBoxContainer
{
	private Control _mainTrack;
	private VBoxContainer _childTracks;
	private List<Control> _childTrackControls = new List<Control>();
	private int _maxFrames;
	private float _pixelsPerFrame;
	private int _childCount;
	private SceneObject _object;
	private string[] _propertyPaths;
	private TimelinePanel _timeline;
	
	// Dragging state
	private bool _isDraggingKeyframe = false;
	private Keyframe _draggedKeyframe = null;
	private string _draggedPropertyPath = null;
	private int _dragStartFrame = 0;

	public KeyframeTrackGroup(int maxFrames, float pixelsPerFrame, int childCount, SceneObject obj, string[] propertyPaths, TimelinePanel timeline)
	{
		_maxFrames = maxFrames;
		_pixelsPerFrame = pixelsPerFrame;
		_childCount = childCount;
		_object = obj;
		_propertyPaths = propertyPaths;
		_timeline = timeline;
		
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		// Main track (collapsed state) - match left side HBoxContainer natural height
		_mainTrack = new Control();
		// Add extra scroll space beyond max frames (50% more) to allow placing keyframes beyond
		var scrollableWidth = maxFrames * pixelsPerFrame * 1.5f;
		_mainTrack.CustomMinimumSize = new Vector2(scrollableWidth, 31); // Match button height
		_mainTrack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		AddChild(_mainTrack);

		// Draw grid background
		_mainTrack.Draw += () => DrawTrackBackground(_mainTrack, false, null);
		
		// Handle clicks on main track
		_mainTrack.GuiInput += (inputEvent) => OnTrackInput(inputEvent, null);

		// Child tracks (expanded state)
		_childTracks = new VBoxContainer();
		_childTracks.Visible = false;
		AddChild(_childTracks);

		// Create child track rows - match left side HBoxContainer natural height
		for (int i = 0; i < childCount; i++)
		{
			var childTrack = new Control();
			childTrack.CustomMinimumSize = new Vector2(scrollableWidth, 31); // Match button height, use scrollable width
			childTrack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_childTracks.AddChild(childTrack);
			_childTrackControls.Add(childTrack);
			
			var propertyPath = propertyPaths[i];
			
			// Draw grid background and keyframes for child
			childTrack.Draw += () => DrawTrackBackground(childTrack, true, propertyPath);
			
			// Handle clicks on child tracks
			childTrack.GuiInput += (inputEvent) => OnTrackInput(inputEvent, propertyPath);
		}
	}

	private void OnTrackInput(InputEvent inputEvent, string propertyPath)
	{
		if (inputEvent is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				// Calculate frame from click position
				float localX = mouseButton.Position.X;
				int frame = Mathf.RoundToInt(localX / _pixelsPerFrame);
				frame = Mathf.Max(0, frame); // Only clamp to 0, allow going past max
				
				if (mouseButton.Pressed)
				{
					// Mouse button pressed
					if (propertyPath != null)
					{
						// Check if clicking on existing keyframe
						var keyframes = _timeline.GetKeyframesForProperty(_object, propertyPath);
						var clickedKeyframe = keyframes.Find(k => Mathf.Abs(k.Frame - frame) <= 1);
						
						if (clickedKeyframe != null && mouseButton.AltPressed)
						{
							// Alt+Click to delete keyframe
							_timeline.RemoveKeyframeForProperty(_object, propertyPath, clickedKeyframe.Frame);
						}
						else if (clickedKeyframe != null)
						{
							// Start dragging existing keyframe
							_isDraggingKeyframe = true;
							_draggedKeyframe = clickedKeyframe;
							_draggedPropertyPath = propertyPath;
							_dragStartFrame = clickedKeyframe.Frame;
						}
						else
						{
							// Add new keyframe
							_timeline.AddKeyframeForProperty(_object, propertyPath, frame);
						}
					}
				}
				else
				{
					// Mouse button released
					if (_isDraggingKeyframe && _draggedKeyframe != null &&_draggedPropertyPath != null)
					{
						// Finish dragging - move keyframe to new position
						if (_draggedKeyframe.Frame != _dragStartFrame)
						{
							_timeline.MoveKeyframe(_object, _draggedPropertyPath, _dragStartFrame, _draggedKeyframe.Frame);
						}
						_isDraggingKeyframe = false;
						_draggedKeyframe = null;
						_draggedPropertyPath = null;
					}
				}
			}
		}
		else if (inputEvent is InputEventMouseMotion mouseMotion && _isDraggingKeyframe && _draggedKeyframe != null)
		{
			// Update keyframe position while dragging
			float localX = mouseMotion.Position.X;
			int newFrame = Mathf.RoundToInt(localX / _pixelsPerFrame);
			newFrame = Mathf.Max(0, newFrame); // Only clamp to 0, allow going past max
			
			if (newFrame != _draggedKeyframe.Frame)
			{
				_draggedKeyframe.Frame = newFrame;
				QueueRedrawTracks();
			}
		}
	}

	private void DrawTrackBackground(Control track, bool isChild, string propertyPath)
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
		
		// Draw keyframes
		if (_timeline != null && _object != null)
		{
			if (propertyPath != null)
			{
				// Child track - draw keyframes for specific property only
				var keyframes = _timeline.GetKeyframesForProperty(_object, propertyPath);
				foreach (var keyframe in keyframes)
				{
					DrawKeyframeDiamond(track, keyframe.Frame);
				}
			}
			else
			{
				// Main track - draw keyframes if ANY child property has a keyframe at that frame
				var allFrames = new HashSet<int>();
				foreach (var propPath in _propertyPaths)
				{
					var keyframes = _timeline.GetKeyframesForProperty(_object, propPath);
					foreach (var keyframe in keyframes)
					{
						allFrames.Add(keyframe.Frame);
					}
				}
				
				foreach (var frame in allFrames)
				{
					DrawKeyframeDiamond(track, frame);
				}
			}
		}
	}
	
	private void DrawKeyframeDiamond(Control track, int frame)
	{
		float x = frame * _pixelsPerFrame;
		float y = track.Size.Y / 2;
		float size = 6f;
		
		// Draw diamond shape
		var points = new Vector2[]
		{
			new Vector2(x, y - size),      // Top
			new Vector2(x + size, y),      // Right
			new Vector2(x, y + size),      // Bottom
			new Vector2(x - size, y)       // Left
		};
		
		// Fill
		track.DrawColoredPolygon(points, new Color(1f, 0.8f, 0.2f));
		
		// Outline
		for (int i = 0; i < points.Length; i++)
		{
			var nextI = (i + 1) % points.Length;
			track.DrawLine(points[i], points[nextI], new Color(0.8f, 0.6f, 0.1f), 1.5f);
		}
	}

	public void SetExpanded(bool expanded)
	{
		_childTracks.Visible = expanded;
	}
	
	public void UpdateMaxFrames(int newMaxFrames)
	{
		_maxFrames = newMaxFrames;
		
		// Add extra scroll space beyond max frames (50% more) to allow placing keyframes beyond
		var scrollableWidth = _maxFrames * _pixelsPerFrame * 1.5f;
		
		// Update track sizes
		if (_mainTrack != null)
		{
			_mainTrack.CustomMinimumSize = new Vector2(scrollableWidth, _mainTrack.CustomMinimumSize.Y);
		}
		
		foreach (var childTrack in _childTrackControls)
		{
			if (childTrack != null)
			{
				childTrack.CustomMinimumSize = new Vector2(scrollableWidth, childTrack.CustomMinimumSize.Y);
			}
		}
		
		QueueRedrawTracks();
	}
	
	public void QueueRedrawTracks()
	{
		_mainTrack?.QueueRedraw();
		foreach (var child in _childTrackControls)
		{
			child?.QueueRedraw();
		}
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
