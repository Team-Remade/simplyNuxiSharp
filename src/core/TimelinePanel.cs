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
	private Control _selectionBoxContainer;
	private Label _timeLabel;
	private ScrollContainer _frameRulerScroll;
	private Control _frameRuler;
	private Button _playPauseButton;
	private PopupMenu _keyframeContextMenu;
	
	// Context menu state
	private Keyframe _contextMenuKeyframe;
	private SceneObject _contextMenuObject;
	private string _contextMenuPropertyPath;
	
	// Timeline settings (frame-based)
	private int _currentFrame = 0;
	private int _maxFrames = 300; // Default 300 frames (10 seconds at 30fps)
	private float _frameRate = 30f; // 30 fps default
	private float _pixelsPerFrame = 5f; // Width per frame in pixels
	private bool _isDraggingPlayhead = false;
	private bool _isPlaying = false;
	private int _playStartFrame = 0; // Frame when play was pressed
	
	// Drag selection state
	private bool _isDragSelecting = false;
	private bool _wasDragging = false; // Track if we actually dragged (moved mouse)
	private Vector2 _dragSelectStart;
	private Vector2 _dragSelectEnd;
	private const float DRAG_THRESHOLD = 3f; // Minimum pixels to move before considering it a drag
	public List<Keyframe> _selectedKeyframes = new List<Keyframe>();
	public Dictionary<Keyframe, (SceneObject obj, string propertyPath)> _keyframeOwners = new Dictionary<Keyframe, (SceneObject, string)>();
	
	// Keyframe dragging state (to prevent drag selection when dragging keyframes)
	public bool _isDraggingKeyframe = false;
	public bool _keyframeWasClicked = false; // Set by keyframe GuiInput, checked in _Input
	
	// Public properties for external access
	public int CurrentFrame => _currentFrame;
	public float Framerate => _frameRate;
	
	/// <summary>
	/// Gets the last keyframe frame number across all properties
	/// </summary>
	public int GetLastKeyframe()
	{
		int lastKeyframe = 0;
		foreach (var kvp in _propertyKeyframes)
		{
			foreach (var keyframe in kvp.Value)
			{
				if (keyframe.Frame > lastKeyframe)
				{
					lastKeyframe = keyframe.Frame;
				}
			}
		}
		return lastKeyframe;
	}
	
	/// <summary>
	/// Sets the current frame and updates the playhead
	/// </summary>
	public void SetCurrentFrame(int frame)
	{
		_currentFrame = Mathf.Clamp(frame, 0, _maxFrames);
		UpdatePlayheadPosition();
		ApplyKeyframesAtCurrentFrame();
	}
	
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
		
		// Setup context menu for keyframes
		SetupKeyframeContextMenu();
	}
	
	private void SetupKeyframeContextMenu()
	{
		_keyframeContextMenu = new PopupMenu();
		_keyframeContextMenu.Name = "KeyframeContextMenu";
		AddChild(_keyframeContextMenu);
		
		// Create submenu for interpolation modes
		var interpolationSubmenu = new PopupMenu();
		interpolationSubmenu.Name = "InterpolationSubmenu";
		interpolationSubmenu.AddItem("Linear", 0);
		interpolationSubmenu.AddItem("Ease In Quadratic", 1);
		interpolationSubmenu.AddItem("Ease Out Quadratic", 2);
		interpolationSubmenu.AddItem("Ease In-Out Quadratic", 3);
		interpolationSubmenu.AddItem("Instant", 4);
		interpolationSubmenu.IndexPressed += OnInterpolationSubmenuIndexPressed;
		_keyframeContextMenu.AddChild(interpolationSubmenu);
		
		// Add main menu items
		_keyframeContextMenu.AddSubmenuNodeItem("Interpolation", interpolationSubmenu, 0);
		_keyframeContextMenu.AddSeparator();
		_keyframeContextMenu.AddItem("Delete Keyframe(s)", 2); // Index 2 because separator counts as index 1
		
		_keyframeContextMenu.IndexPressed += OnKeyframeContextMenuIndexPressed;
	}
	
	private void OnInterpolationSubmenuIndexPressed(long index)
	{
		if (_contextMenuKeyframe == null || _contextMenuObject == null || _contextMenuPropertyPath == null)
			return;
		
		string interpolationType = index switch
		{
			0 => "linear",
			1 => "ease-in-quadratic",
			2 => "ease-out-quadratic",
			3 => "ease-in-out-quadratic",
			4 => "instant",
			_ => "linear"
		};
		
		// Update the keyframe's interpolation type
		_contextMenuKeyframe.InterpolationType = interpolationType;
		
		// Save to SceneObject
		SaveKeyframesToObject(_contextMenuObject, _contextMenuPropertyPath);
	}
	
	private void OnKeyframeContextMenuIndexPressed(long index)
	{
		if (_contextMenuKeyframe == null || _contextMenuObject == null || _contextMenuPropertyPath == null)
			return;
		
		switch (index)
		{
			case 2: // Delete Keyframe(s) - index 2 because separator is index 1
				DeleteSelectedKeyframes();
				break;
		}
	}
	
	public void ShowKeyframeContextMenu(Keyframe keyframe, SceneObject obj, string propertyPath, Vector2 globalPosition)
	{
		_contextMenuKeyframe = keyframe;
		_contextMenuObject = obj;
		_contextMenuPropertyPath = propertyPath;
		
		// If the keyframe is not already selected, add it to the selection
		// This ensures that when "Delete Keyframe(s)" is clicked, it will be deleted
		if (!_selectedKeyframes.Contains(keyframe))
		{
			// Clear previous selection and select only this keyframe
			_selectedKeyframes.Clear();
			_keyframeOwners.Clear();
			_selectedKeyframes.Add(keyframe);
			_keyframeOwners[keyframe] = (obj, propertyPath);
			
			// Redraw tracks to show the new selection
			foreach (var track in _singlePropertyTracks)
			{
				track.QueueRedraw();
			}
			foreach (var prop in _properties)
			{
				if (prop.TrackGroup != null)
				{
					prop.TrackGroup.QueueRedrawTracks();
				}
			}
		}
		
		_keyframeContextMenu.Position = (Vector2I)globalPosition;
		_keyframeContextMenu.Popup();
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
		
		// Selection box container overlay (on top of everything)
		_selectionBoxContainer = new Control();
		_selectionBoxContainer.SetAnchorsPreset(LayoutPreset.TopLeft);
		_selectionBoxContainer.MouseFilter = MouseFilterEnum.Ignore; // Clicks pass through
		_selectionBoxContainer.Position = new Vector2(0, 0);
		_selectionBoxContainer.Size = new Vector2(10000, 10000); // Large size
		_selectionBoxContainer.ZIndex = 101; // Above playhead
		_selectionBoxContainer.Draw += DrawSelectionBox;
		scrollAndPlayheadContainer.AddChild(_selectionBoxContainer);

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

		// Timeline scrubbing and drag selection via click on tracks
		_keyframesTracksContainer.GuiInput += OnTimelineInput;
		_keyframesTracksContainer.GuiInput += OnKeyframesContainerInput;

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
				// Don't handle timeline scrubbing if we're starting a drag selection
				// The drag selection handler will take care of it
				// We'll handle the click in the mouse release if it wasn't a drag
			}
			else if (mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
			{
				// Only move playhead on release if we weren't drag selecting
				// Check _wasDragging to see if this was actually a drag operation
				if (!_wasDragging)
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
	}
	
	private void OnKeyframesContainerInput(InputEvent @event)
	{
		// This handler runs AFTER child GuiInput handlers (like keyframes)
		// So if a keyframe consumed the event, this won't be called
		// And if _isDraggingKeyframe is set, we know not to start drag selection
		
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					// Don't start drag selection if dragging playhead or keyframe
					if (_isDraggingPlayhead || _isDraggingKeyframe)
					{
						GD.Print("[OnKeyframesContainerInput] Not starting drag selection - playhead or keyframe is being dragged");
						return;
					}
					
					// Start potential drag selection
					_isDragSelecting = true;
					_wasDragging = false;
					_dragSelectStart = mouseButton.Position;
					_dragSelectEnd = mouseButton.Position;
					
					// Clear selection if not holding Shift
					if (!mouseButton.ShiftPressed)
					{
						_selectedKeyframes.Clear();
						_keyframeOwners.Clear();
					}
					
					GD.Print($"[OnKeyframesContainerInput] Started potential drag selection at {mouseButton.Position}");
					_selectionBoxContainer?.QueueRedraw();
				}
				else
				{
					// End drag selection
					if (_isDragSelecting)
					{
						GD.Print($"[OnKeyframesContainerInput] Ending drag selection, _wasDragging: {_wasDragging}");
						_isDragSelecting = false;
						
						// If we didn't actually drag (just clicked), clear selection
						if (!_wasDragging)
						{
							_selectedKeyframes.Clear();
							_keyframeOwners.Clear();
							// Redraw tracks to update keyframe colors
							foreach (var track in _singlePropertyTracks)
							{
								track.QueueRedraw();
							}
						}
						
						_wasDragging = false;
						_selectionBoxContainer?.QueueRedraw();
					}
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			// Only handle motion if we're drag selecting and not dragging keyframe/playhead
			if (_isDragSelecting && !_isDraggingKeyframe && !_isDraggingPlayhead)
			{
				// Check if we've moved enough to consider this a drag
				float distance = _dragSelectStart.DistanceTo(mouseMotion.Position);
				if (distance >= DRAG_THRESHOLD && !_wasDragging)
				{
					_wasDragging = true;
					GD.Print($"[OnKeyframesContainerInput] Started dragging (moved {distance} pixels)");
				}
				
				// Update drag selection box
				_dragSelectEnd = mouseMotion.Position;
				
				// Only update selection if we're actually dragging
				if (_wasDragging)
				{
					// Update selected keyframes based on selection box
					UpdateDragSelection();
				}
				
				_selectionBoxContainer?.QueueRedraw();
			}
			else if (_isDragSelecting && (_isDraggingKeyframe || _isDraggingPlayhead))
			{
				// Cancel drag selection if keyframe or playhead drag started
				GD.Print("[OnKeyframesContainerInput] Cancelling drag selection - keyframe or playhead drag detected");
				_isDragSelecting = false;
				_wasDragging = false;
				_selectionBoxContainer?.QueueRedraw();
			}
		}
	}
	
	private void UpdateDragSelection()
	{
		// Calculate selection rectangle accounting for scroll
		var minX = Mathf.Min(_dragSelectStart.X, _dragSelectEnd.X) + _keyframesScroll.ScrollHorizontal;
		var maxX = Mathf.Max(_dragSelectStart.X, _dragSelectEnd.X) + _keyframesScroll.ScrollHorizontal;
		var minY = Mathf.Min(_dragSelectStart.Y, _dragSelectEnd.Y) + _keyframesScroll.ScrollVertical;
		var maxY = Mathf.Max(_dragSelectStart.Y, _dragSelectEnd.Y) + _keyframesScroll.ScrollVertical;
		
		var minFrame = Mathf.FloorToInt(minX / _pixelsPerFrame);
		var maxFrame = Mathf.CeilToInt(maxX / _pixelsPerFrame);
		
		// Clear current selection if not holding Shift
		if (!Input.IsKeyPressed(Key.Shift))
		{
			_selectedKeyframes.Clear();
			_keyframeOwners.Clear();
		}
		
		// Find all keyframes within the selection box
		foreach (var kvp in _propertyKeyframes)
		{
			var fullPath = kvp.Key;
			var keyframes = kvp.Value;
			
			// Parse the full path to get object and property
			var pathParts = fullPath.Split('.');
			if (pathParts.Length < 2) continue;
			
			var objectIdStr = pathParts[0];
			if (!ulong.TryParse(objectIdStr, out ulong objectId)) continue;
			
			// Find the object
			SceneObject targetObject = null;
			string propertyPath = null;
			
			foreach (var prop in _properties)
			{
				if (prop.Object.GetInstanceId() == objectId)
				{
					targetObject = prop.Object;
					
					// Reconstruct property path
					if (pathParts.Length == 2)
					{
						propertyPath = pathParts[1];
					}
					else if (pathParts.Length >= 3)
					{
						propertyPath = $"{pathParts[1]}.{pathParts[2]}";
					}
					break;
				}
			}
			
			if (targetObject == null || propertyPath == null) continue;
			
			// Check each keyframe
			foreach (var keyframe in keyframes)
			{
				if (keyframe.Frame >= minFrame && keyframe.Frame <= maxFrame)
				{
					// Check if keyframe is within Y bounds (track height)
					// This is a simplified check - you might want to calculate exact track positions
					if (!_selectedKeyframes.Contains(keyframe))
					{
						_selectedKeyframes.Add(keyframe);
						_keyframeOwners[keyframe] = (targetObject, propertyPath);
					}
				}
			}
		}
		
		// Redraw all tracks to update selection visuals
		foreach (var track in _singlePropertyTracks)
		{
			track.QueueRedraw();
		}
		
		// Also redraw property group tracks
		foreach (var prop in _properties)
		{
			if (prop.TrackGroup != null)
			{
				prop.TrackGroup.QueueRedrawTracks();
			}
		}
	}
	
	private void DrawSelectionBox()
	{
		if (_isDragSelecting && _wasDragging && _keyframesScroll != null)
		{
			// Convert local coordinates to screen coordinates accounting for scroll
			var scrollPos = _keyframesScroll.GlobalPosition - _selectionBoxContainer.GlobalPosition;
			
			var minX = Mathf.Min(_dragSelectStart.X, _dragSelectEnd.X) - _keyframesScroll.ScrollHorizontal + scrollPos.X;
			var minY = Mathf.Min(_dragSelectStart.Y, _dragSelectEnd.Y) - _keyframesScroll.ScrollVertical + scrollPos.Y;
			var maxX = Mathf.Max(_dragSelectStart.X, _dragSelectEnd.X) - _keyframesScroll.ScrollHorizontal + scrollPos.X;
			var maxY = Mathf.Max(_dragSelectStart.Y, _dragSelectEnd.Y) - _keyframesScroll.ScrollVertical + scrollPos.Y;
			
			var rect = new Rect2(
				minX,
				minY,
				maxX - minX,
				maxY - minY
			);
			
			// Draw selection box
			_selectionBoxContainer.DrawRect(rect, new Color(0.3f, 0.6f, 1f, 0.2f), true);
			_selectionBoxContainer.DrawRect(rect, new Color(0.3f, 0.6f, 1f, 0.8f), false, 2f);
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

		// Handle drag selection in _Input
		// We need to use _Input because GuiInput on the container doesn't receive events
		// when clicking on child controls (tracks)
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
			{
				_isDraggingPlayhead = false;
				
				// Reset drag select state and keyframe click flag
				_dragSelectStart = Vector2.Zero;
				_keyframeWasClicked = false;
				
				// End drag selection
				if (_isDragSelecting)
				{
					GD.Print($"[TimelinePanel._Input] Ending drag selection, _wasDragging: {_wasDragging}");
					_isDragSelecting = false;
					
					// If we didn't actually drag (just clicked), clear selection
					if (!_wasDragging)
					{
						_selectedKeyframes.Clear();
						_keyframeOwners.Clear();
						// Redraw tracks to update keyframe colors
						foreach (var track in _singlePropertyTracks)
						{
							track.QueueRedraw();
						}
					}
					
					_wasDragging = false;
					_selectionBoxContainer?.QueueRedraw();
				}
			}
			else if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
			{
				// Don't prepare for drag selection yet - wait for mouse motion
				// This allows GuiInput handlers to set _isDraggingKeyframe first
				// We'll prepare in the motion handler if needed
			}
		}
		else if (@event is InputEventMouseMotion mouseMotionEvent)
		{
			// Check if we should start or continue drag selection
			// Don't start if a keyframe was clicked
			if (!_isDragSelecting && !_isDraggingPlayhead && Input.IsMouseButtonPressed(MouseButton.Left))
			{
				if (_keyframeWasClicked)
					return;
				
				// Check if we're in the keyframes area
				if (_keyframesScroll != null && _keyframesTracksContainer != null)
				{
					var scrollRect = _keyframesScroll.GetGlobalRect();
					if (scrollRect.HasPoint(mouseMotionEvent.GlobalPosition))
					{
						var localPos = _keyframesTracksContainer.GetGlobalTransform().AffineInverse() * mouseMotionEvent.GlobalPosition;
						
						// If we don't have a drag start yet, set it now (first motion after click)
						if (_dragSelectStart == Vector2.Zero)
						{
							_dragSelectStart = localPos;
							_dragSelectEnd = localPos;
						}
					}
				}
			}
			
			// Try to start drag selection if we have a drag start
			if (!_isDragSelecting && _dragSelectStart != Vector2.Zero && !_isDraggingKeyframe && !_isDraggingPlayhead)
			{
				if (_keyframesScroll != null && _keyframesTracksContainer != null)
				{
					var scrollRect = _keyframesScroll.GetGlobalRect();
					if (scrollRect.HasPoint(mouseMotionEvent.GlobalPosition))
					{
						var localPos = _keyframesTracksContainer.GetGlobalTransform().AffineInverse() * mouseMotionEvent.GlobalPosition;
						float distance = _dragSelectStart.DistanceTo(localPos);
						
						// Start drag selection if we've moved enough
						if (distance >= DRAG_THRESHOLD)
						{
							_isDragSelecting = true;
							_wasDragging = false;
							
							// Clear selection if not holding Shift
							if (!Input.IsKeyPressed(Key.Shift))
							{
								_selectedKeyframes.Clear();
								_keyframeOwners.Clear();
							}
							
							GD.Print($"[TimelinePanel._Input] Started drag selection after deferred check (moved {distance} pixels)");
						}
					}
				}
			}
			
			// Update drag selection if active
			if (_isDragSelecting)
			{
				// Don't update drag selection if we're dragging the playhead or a keyframe
				if (_isDraggingPlayhead || _isDraggingKeyframe)
				{
					_isDragSelecting = false;
					_selectionBoxContainer?.QueueRedraw();
					return;
				}
					
				// Update drag selection
				if (_keyframesTracksContainer != null)
				{
					var localPos = _keyframesTracksContainer.GetGlobalTransform().AffineInverse() * mouseMotionEvent.GlobalPosition;
					
					// Check if we've moved enough to consider this a drag
					float distance = _dragSelectStart.DistanceTo(localPos);
					if (distance >= DRAG_THRESHOLD && !_wasDragging)
					{
						_wasDragging = true;
						GD.Print($"[TimelinePanel._Input] Started dragging (moved {distance} pixels)");
					}
					
					_dragSelectEnd = localPos;
					
					// Only update selection if we're actually dragging
					if (_wasDragging)
					{
						UpdateDragSelection();
					}
					
					_selectionBoxContainer?.QueueRedraw();
				}
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

		// Alpha property (non-collapsible single property)
		AddSingleProperty(obj, "Alpha", "material.alpha");

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
				
				// Check if keyframe is selected
				bool isSelected = _selectedKeyframes.Contains(keyframe);
				
				// Draw diamond shape
				var points = new Vector2[]
				{
					new Vector2(x, y - size),
					new Vector2(x + size, y),
					new Vector2(x, y + size),
					new Vector2(x - size, y)
				};
				
				// Use different colors for selected keyframes
				var fillColor = isSelected ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.8f, 0.2f);
				var outlineColor = isSelected ? new Color(0.2f, 0.4f, 0.8f) : new Color(0.8f, 0.6f, 0.1f);
				
				track.DrawColoredPolygon(points, fillColor);
				
				for (int i = 0; i < points.Length; i++)
				{
					var nextI = (i + 1) % points.Length;
					track.DrawLine(points[i], points[nextI], outlineColor, 1.5f);
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
						
						GD.Print($"[Track {propertyPath}] Click at frame {frame}, found keyframe: {clickedKeyframe != null}");
						
						if (clickedKeyframe != null && mouseButton.AltPressed)
						{
							// Alt+Click to remove keyframe
							RemoveKeyframeForProperty(obj, propertyPath, clickedKeyframe.Frame);
							track.AcceptEvent(); // Consume the event
							GD.Print($"[Track {propertyPath}] Removed keyframe, event consumed");
						}
						else if (clickedKeyframe != null)
						{
							// Click on keyframe to select/drag it
							isDragging = true;
							_isDraggingKeyframe = true; // Set global flag
							_keyframeWasClicked = true; // Set flag to prevent drag selection
							draggedKeyframe = clickedKeyframe;
							dragStartFrame = clickedKeyframe.Frame;
							
							// Handle selection
							if (!mouseButton.ShiftPressed)
							{
								_selectedKeyframes.Clear();
								_keyframeOwners.Clear();
							}
							
							if (!_selectedKeyframes.Contains(clickedKeyframe))
							{
								_selectedKeyframes.Add(clickedKeyframe);
								_keyframeOwners[clickedKeyframe] = (obj, propertyPath);
							}
							
							// Redraw all tracks to update selection visuals
							foreach (var t in _singlePropertyTracks)
							{
								t.QueueRedraw();
							}
							
							track.AcceptEvent(); // Consume the event
							GD.Print($"[Track {propertyPath}] Started dragging keyframe, _keyframeWasClicked set to true");
						}
						else
						{
							GD.Print($"[Track {propertyPath}] No keyframe clicked, event NOT consumed (should bubble up)");
						}
						// If no keyframe was clicked, don't consume the event - let it bubble up for drag selection
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
							_isDraggingKeyframe = false; // Clear global flag
							draggedKeyframe = null;
							track.AcceptEvent(); // Consume the event
						}
						// If we weren't dragging a keyframe, don't consume - let it bubble for drag selection
					}
				}
				else if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
				{
					// Right-click to open context menu
					float localX = mouseButton.Position.X;
					int frame = Mathf.RoundToInt(localX / _pixelsPerFrame);
					frame = Mathf.Max(0, frame);
					
					var keyframes = GetKeyframesForProperty(obj, propertyPath);
					var clickedKeyframe = keyframes.Find(k => Mathf.Abs(k.Frame - frame) <= 1);
					
					if (clickedKeyframe != null)
					{
						ShowKeyframeContextMenu(clickedKeyframe, obj, propertyPath, mouseButton.GlobalPosition);
						track.AcceptEvent(); // Consume the event
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
				track.AcceptEvent(); // Consume the event while dragging
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
	
	private void DeleteSelectedKeyframes()
	{
		// If there are selected keyframes, delete all of them
		if (_selectedKeyframes.Count > 0)
		{
			// Group keyframes by their owner (object + property path)
			var keyframesToDelete = new Dictionary<(SceneObject, string), List<Keyframe>>();
			
			foreach (var keyframe in _selectedKeyframes)
			{
				if (_keyframeOwners.TryGetValue(keyframe, out var owner))
				{
					var key = (owner.obj, owner.propertyPath);
					if (!keyframesToDelete.ContainsKey(key))
					{
						keyframesToDelete[key] = new List<Keyframe>();
					}
					keyframesToDelete[key].Add(keyframe);
				}
			}
			
			// Delete all keyframes
			foreach (var kvp in keyframesToDelete)
			{
				var obj = kvp.Key.Item1;
				var propertyPath = kvp.Key.Item2;
				var keyframes = kvp.Value;
				
				var fullPath = $"{obj.GetInstanceId()}.{propertyPath}";
				
				if (_propertyKeyframes.ContainsKey(fullPath))
				{
					foreach (var keyframe in keyframes)
					{
						_propertyKeyframes[fullPath].Remove(keyframe);
					}
					
					// Save to SceneObject
					SaveKeyframesToObject(obj, propertyPath);
				}
			}
			
			// Clear selection
			_selectedKeyframes.Clear();
			_keyframeOwners.Clear();
			
			// Redraw all tracks to update visuals
			foreach (var track in _singlePropertyTracks)
			{
				track.QueueRedraw();
			}
			
			// Also redraw property group tracks
			foreach (var prop in _properties)
			{
				if (prop.TrackGroup != null)
				{
					prop.TrackGroup.QueueRedrawTracks();
				}
			}
		}
		// Otherwise, delete just the context menu keyframe
		else if (_contextMenuKeyframe != null && _contextMenuObject != null && _contextMenuPropertyPath != null)
		{
			RemoveKeyframeForProperty(_contextMenuObject, _contextMenuPropertyPath, _contextMenuKeyframe.Frame);
		}
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
				case "material":
					if (component == "alpha")
					{
						// Get alpha from first surface material if available
						var meshInstances = obj.GetMeshInstancesRecursively(obj.Visual);
						if (meshInstances.Count > 0 && meshInstances[0].Mesh != null && meshInstances[0].Mesh.GetSurfaceCount() > 0)
						{
							var material = meshInstances[0].Mesh.SurfaceGetMaterial(0);
							if (material is StandardMaterial3D stdMat)
							{
								return stdMat.AlbedoColor.A;
							}
						}
						return 1f; // Default to fully opaque
					}
					break;
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
				// For other properties, use interpolation based on the previous keyframe's type
				if (prevKeyframe != null && nextKeyframe != null && prevKeyframe.Frame != nextKeyframe.Frame)
				{
					// Interpolate between keyframes using the interpolation type of the previous keyframe
					float t = (_currentFrame - prevKeyframe.Frame) / (float)(nextKeyframe.Frame - prevKeyframe.Frame);
					float prevValue = Convert.ToSingle(prevKeyframe.Value);
					float nextValue = Convert.ToSingle(nextKeyframe.Value);
					
					// Apply interpolation based on type
					float interpolatedT = ApplyInterpolation(t, prevKeyframe.InterpolationType);
					value = Mathf.Lerp(prevValue, nextValue, interpolatedT);
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
	
	private float ApplyInterpolation(float t, string interpolationType)
	{
		return interpolationType switch
		{
			"linear" => t,
			"ease-in-quadratic" => EaseInQuadratic(t),
			"ease-out-quadratic" => EaseOutQuadratic(t),
			"ease-in-out-quadratic" => EaseInOutQuadratic(t),
			"instant" => 0f, // Step function - stay at previous value until end
			_ => t // Default to linear
		};
	}
	
	private float EaseInQuadratic(float t)
	{
		return t * t;
	}
	
	private float EaseOutQuadratic(float t)
	{
		return 1f - (1f - t) * (1f - t);
	}
	
	private float EaseInOutQuadratic(float t)
	{
		return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
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
					
				case "material":
					if (component == "alpha")
					{
						// Update all materials on all surfaces of the object
						var meshInstances = obj.GetMeshInstancesRecursively(obj.Visual);
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
									color.A = value;
									stdMat.AlbedoColor = color;
									
									// Don't automatically change transparency mode - let user control it via dropdown
								}
							}
						}
					}
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
	private Dictionary<Keyframe, int> _selectedKeyframesStartFrames = new Dictionary<Keyframe, int>();

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
		Control sourceControl = null;
		if (inputEvent is InputEventMouseButton mb)
		{
			// Find which control received the event
			if (propertyPath != null)
			{
				int index = System.Array.IndexOf(_propertyPaths, propertyPath);
				if (index >= 0 && index < _childTrackControls.Count)
					sourceControl = _childTrackControls[index];
			}
			else
			{
				sourceControl = _mainTrack;
			}
		}
		
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
							sourceControl?.AcceptEvent(); // Consume the event
						}
						else if (clickedKeyframe != null)
						{
							// Start dragging existing keyframe
							_isDraggingKeyframe = true;
							_timeline._isDraggingKeyframe = true; // Set global flag
							_timeline._keyframeWasClicked = true; // Set flag to prevent drag selection
							_draggedKeyframe = clickedKeyframe;
							_draggedPropertyPath = propertyPath;
							_dragStartFrame = clickedKeyframe.Frame;
							
							// Handle selection
							bool keyframeAlreadySelected = _timeline._selectedKeyframes.Contains(clickedKeyframe);
							
							if (!mouseButton.ShiftPressed && !keyframeAlreadySelected)
							{
								// Only clear selection if the clicked keyframe is not already selected
								_timeline._selectedKeyframes.Clear();
								_timeline._keyframeOwners.Clear();
							}
							
							if (!_timeline._selectedKeyframes.Contains(clickedKeyframe))
							{
								_timeline._selectedKeyframes.Add(clickedKeyframe);
								_timeline._keyframeOwners[clickedKeyframe] = (_object, propertyPath);
							}
							
							// Store start frames for all selected keyframes
							_selectedKeyframesStartFrames.Clear();
							foreach (var kf in _timeline._selectedKeyframes)
							{
								_selectedKeyframesStartFrames[kf] = kf.Frame;
							}
							
							// Redraw all tracks to update selection visuals
							QueueRedrawTracks();
							
							sourceControl?.AcceptEvent(); // Consume the event
						}
						else
						{
							// Clicked on empty area (no keyframe) - clear selection if not holding Shift
							if (!mouseButton.ShiftPressed)
							{
								_timeline._selectedKeyframes.Clear();
								_timeline._keyframeOwners.Clear();
								
								// Redraw all tracks to update selection visuals
								QueueRedrawTracks();
							}
							// Don't consume - let it bubble for drag selection
						}
					}
					else
					{
						// Clicked on main track (no specific property) - clear selection if not holding Shift
						if (!mouseButton.ShiftPressed)
						{
							_timeline._selectedKeyframes.Clear();
							_timeline._keyframeOwners.Clear();
							
							// Redraw all tracks to update selection visuals
							QueueRedrawTracks();
						}
						// Don't consume - let it bubble for drag selection
					}
				}
				else
				{
					// Mouse button released
					if (_isDraggingKeyframe && _draggedKeyframe != null &&_draggedPropertyPath != null)
					{
						// Finish dragging - move all selected keyframes to their new positions
						foreach (var kf in _timeline._selectedKeyframes)
						{
							if (_selectedKeyframesStartFrames.TryGetValue(kf, out int startFrame))
							{
								if (kf.Frame != startFrame)
								{
									// Find the owner of this keyframe
									if (_timeline._keyframeOwners.TryGetValue(kf, out var owner))
									{
										_timeline.MoveKeyframe(owner.obj, owner.propertyPath, startFrame, kf.Frame);
									}
								}
							}
						}
						
						_isDraggingKeyframe = false;
						_timeline._isDraggingKeyframe = false; // Clear global flag
						_draggedKeyframe = null;
						_draggedPropertyPath = null;
						_selectedKeyframesStartFrames.Clear();
						sourceControl?.AcceptEvent(); // Consume the event
					}
					// If we weren't dragging, don't consume - let it bubble for drag selection
				}
			}
			else if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
			{
				// Right-click to open context menu
				if (propertyPath != null)
				{
					float localX = mouseButton.Position.X;
					int frame = Mathf.RoundToInt(localX / _pixelsPerFrame);
					frame = Mathf.Max(0, frame);
					
					var keyframes = _timeline.GetKeyframesForProperty(_object, propertyPath);
					var clickedKeyframe = keyframes.Find(k => Mathf.Abs(k.Frame - frame) <= 1);
					
					if (clickedKeyframe != null)
					{
						_timeline.ShowKeyframeContextMenu(clickedKeyframe, _object, propertyPath, mouseButton.GlobalPosition);
						sourceControl?.AcceptEvent(); // Consume the event
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
				// Calculate the offset from the original position
				int offset = newFrame - _dragStartFrame;
				
				// Move all selected keyframes by the same offset
				foreach (var kf in _timeline._selectedKeyframes)
				{
					if (_selectedKeyframesStartFrames.TryGetValue(kf, out int startFrame))
					{
						int targetFrame = startFrame + offset;
						kf.Frame = Mathf.Max(0, targetFrame); // Clamp to 0
					}
				}
				
				QueueRedrawTracks();
			}
			sourceControl?.AcceptEvent(); // Consume the event while dragging
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
					DrawKeyframeDiamond(track, keyframe, propertyPath);
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
					DrawKeyframeDiamond(track, null, null, frame);
				}
			}
		}
	}
	
	private void DrawKeyframeDiamond(Control track, Keyframe keyframe, string propertyPath, int? frameOverride = null)
	{
		int frame = frameOverride ?? keyframe?.Frame ?? 0;
		float x = frame * _pixelsPerFrame;
		float y = track.Size.Y / 2;
		float size = 6f;
		
		// Check if this specific keyframe is selected
		bool isSelected = false;
		if (keyframe != null)
		{
			// Check the specific keyframe instance
			isSelected = _timeline._selectedKeyframes.Contains(keyframe);
		}
		else if (frameOverride.HasValue)
		{
			// For main track, check if ANY keyframe at this frame is selected
			foreach (var propPath in _propertyPaths)
			{
				var keyframes = _timeline.GetKeyframesForProperty(_object, propPath);
				var kf = keyframes.Find(k => k.Frame == frameOverride.Value);
				if (kf != null && _timeline._selectedKeyframes.Contains(kf))
				{
					isSelected = true;
					break;
				}
			}
		}
		
		// Draw diamond shape
		var points = new Vector2[]
		{
			new Vector2(x, y - size),      // Top
			new Vector2(x + size, y),      // Right
			new Vector2(x, y + size),      // Bottom
			new Vector2(x - size, y)       // Left
		};
		
		// Use different colors for selected keyframes
		var fillColor = isSelected ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.8f, 0.2f);
		var outlineColor = isSelected ? new Color(0.2f, 0.4f, 0.8f) : new Color(0.8f, 0.6f, 0.1f);
		
		// Fill
		track.DrawColoredPolygon(points, fillColor);
		
		// Outline
		for (int i = 0; i < points.Length; i++)
		{
			var nextI = (i + 1) % points.Length;
			track.DrawLine(points[i], points[nextI], outlineColor, 1.5f);
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
