using Godot;
using System;
using System.Collections.Generic;
using FFMpegCore;
using FFMpegCore.Enums;

namespace simplyRemadeNuxi.core;

public partial class PreviewViewport : Control
{
	[Export] public SubViewportContainer ViewportContainer;
	[Export] public SubViewport PreviewSubViewport;
	[Export] public Camera3D PreviewCamera;
	[Export] public Button ToggleModeButton;
	[Export] public OptionButton CameraDropdown;
	[Export] public Control HeaderBar;
	[Export] public Panel MainPanel;
	[Export] public Control ResizeBorder;
	[Export] public Label FpsLabel;
	
	private Window _dedicatedWindow;
	private bool _isInDedicatedWindow = false;
	private bool _isDragging = false;
	private Vector2 _dragOffset = Vector2.Zero;
	private Control _originalParent;
	private Vector2 _originalPosition;
	private Vector2 _originalSize;
	
	// Resize properties
	private bool _isResizing = false;
	private ResizeEdge _resizeEdge = ResizeEdge.None;
	private Vector2 _resizeStartPos = Vector2.Zero;
	private Vector2 _resizeStartSize = Vector2.Zero;
	private const float RESIZE_HANDLE_SIZE = 8f;
	private const float RESIZE_BORDER_WIDTH = 4f;

	private bool rendering;
	
	private enum ResizeEdge
	{
		None,
		Left,
		Right,
		Top,
		Bottom,
		TopLeft,
		TopRight,
		BottomLeft,
		BottomRight
	}
	
	// Corner snap positions
	private enum Corner
	{
		BottomRight,
		BottomLeft,
		TopRight,
		TopLeft
	}
	
	private Corner _currentCorner = Corner.BottomRight;
	private const float SNAP_DISTANCE = 50f;
	
	private SubViewport _mainViewport;
	private Camera3D _mainCamera;
	
	// Camera tracking
	private List<CameraSceneObject> _sceneCameras = new List<CameraSceneObject>();
	private CameraSceneObject _activeSceneCamera = null;
	private bool _useWorkCamera = true;
	
	public override void _Ready()
	{
		// Setup camera with all cull layers except layer 2
		if (PreviewCamera != null)
		{
			// Enable all 20 cull layers
			uint cullMask = 0;
			for (int i = 1; i <= 20; i++)
			{
				if (i != 2) // Exclude layer 2
				{
					cullMask |= (uint)(1 << (i - 1));
				}
			}
			PreviewCamera.CullMask = cullMask;
			
			// Set initial camera position
			PreviewCamera.Position = new Vector3(3, 3, 3);
			PreviewCamera.LookAt(Vector3.Zero, Vector3.Up);
		}
		
		// Connect toggle button
		if (ToggleModeButton != null)
		{
			ToggleModeButton.Pressed += OnToggleModePressed;
			ToggleModeButton.Text = "Pop Out";
		}
		
		// Setup camera dropdown
		if (CameraDropdown != null)
		{
			CameraDropdown.ItemSelected += OnCameraDropdownChanged;
			RefreshCameraDropdown();
		}
		
		// Setup dragging on header bar
		if (HeaderBar != null)
		{
			HeaderBar.GuiInput += OnHeaderBarInput;
		}
		
		// Store original parent and position
		_originalParent = GetParentControl();
		_originalPosition = Position;
		_originalSize = Size;
		
		// Position at bottom right by default
		PositionAtCorner(Corner.BottomRight);
		
		// Ensure we're on top
		ZIndex = 100;
		
		// Enable mouse filter to receive input events for resizing
		MouseFilter = MouseFilterEnum.Pass;
	}
	
	public override void _GuiInput(InputEvent @event)
	{
		// Only handle resizing when docked
		if (_isInDedicatedWindow)
			return;
			
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					var edge = GetResizeEdgeAtPosition(mouseButton.Position);
					if (edge != ResizeEdge.None)
					{
						_isResizing = true;
						_resizeEdge = edge;
						_resizeStartPos = GlobalPosition;
						_resizeStartSize = Size;
					}
				}
				else
				{
					_isResizing = false;
					_resizeEdge = ResizeEdge.None;
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			if (_isResizing)
			{
				HandleResize(mouseMotion.Relative);
			}
			else
			{
				// Update cursor based on hover position
				UpdateCursorForPosition(mouseMotion.Position);
			}
		}
	}
	
	private void OnHeaderBarInput(InputEvent @event)
	{
		// Only allow dragging when not in dedicated window and not resizing
		if (_isInDedicatedWindow || _isResizing)
			return;
			
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					_isDragging = true;
					_dragOffset = mouseButton.Position;
				}
				else
				{
					_isDragging = false;
					// Snap to nearest corner
					SnapToNearestCorner();
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			// Update position during drag
			GlobalPosition += mouseMotion.Relative;
		}
	}
	
	private ResizeEdge GetResizeEdgeAtPosition(Vector2 pos)
	{
		bool left = pos.X <= RESIZE_HANDLE_SIZE;
		bool right = pos.X >= Size.X - RESIZE_HANDLE_SIZE;
		bool top = pos.Y <= RESIZE_HANDLE_SIZE;
		bool bottom = pos.Y >= Size.Y - RESIZE_HANDLE_SIZE;
		
		if (top && left) return ResizeEdge.TopLeft;
		if (top && right) return ResizeEdge.TopRight;
		if (bottom && left) return ResizeEdge.BottomLeft;
		if (bottom && right) return ResizeEdge.BottomRight;
		if (left) return ResizeEdge.Left;
		if (right) return ResizeEdge.Right;
		if (top) return ResizeEdge.Top;
		if (bottom) return ResizeEdge.Bottom;
		
		return ResizeEdge.None;
	}
	
	private void UpdateCursorForPosition(Vector2 pos)
	{
		var edge = GetResizeEdgeAtPosition(pos);
		CursorShape cursor = CursorShape.Arrow;
		
		switch (edge)
		{
			case ResizeEdge.Left:
			case ResizeEdge.Right:
				cursor = CursorShape.Hsize;
				break;
			case ResizeEdge.Top:
			case ResizeEdge.Bottom:
				cursor = CursorShape.Vsize;
				break;
			case ResizeEdge.TopLeft:
			case ResizeEdge.BottomRight:
				cursor = CursorShape.Fdiagsize;
				break;
			case ResizeEdge.TopRight:
			case ResizeEdge.BottomLeft:
				cursor = CursorShape.Bdiagsize;
				break;
		}
		
		MouseDefaultCursorShape = cursor;
	}
	
	private void HandleResize(Vector2 relative)
	{
		Vector2 newPos = Position;
		Vector2 newSize = Size;
		Vector2 minSize = CustomMinimumSize;
		
		if (minSize == Vector2.Zero)
			minSize = new Vector2(200, 150); // Default minimum size
		
		switch (_resizeEdge)
		{
			case ResizeEdge.Left:
				newPos.X += relative.X;
				newSize.X -= relative.X;
				if (newSize.X < minSize.X)
				{
					newPos.X -= (minSize.X - newSize.X);
					newSize.X = minSize.X;
				}
				break;
				
			case ResizeEdge.Right:
				newSize.X += relative.X;
				if (newSize.X < minSize.X)
					newSize.X = minSize.X;
				break;
				
			case ResizeEdge.Top:
				newPos.Y += relative.Y;
				newSize.Y -= relative.Y;
				if (newSize.Y < minSize.Y)
				{
					newPos.Y -= (minSize.Y - newSize.Y);
					newSize.Y = minSize.Y;
				}
				break;
				
			case ResizeEdge.Bottom:
				newSize.Y += relative.Y;
				if (newSize.Y < minSize.Y)
					newSize.Y = minSize.Y;
				break;
				
			case ResizeEdge.TopLeft:
				newPos.X += relative.X;
				newSize.X -= relative.X;
				newPos.Y += relative.Y;
				newSize.Y -= relative.Y;
				if (newSize.X < minSize.X)
				{
					newPos.X -= (minSize.X - newSize.X);
					newSize.X = minSize.X;
				}
				if (newSize.Y < minSize.Y)
				{
					newPos.Y -= (minSize.Y - newSize.Y);
					newSize.Y = minSize.Y;
				}
				break;
				
			case ResizeEdge.TopRight:
				newSize.X += relative.X;
				newPos.Y += relative.Y;
				newSize.Y -= relative.Y;
				if (newSize.X < minSize.X)
					newSize.X = minSize.X;
				if (newSize.Y < minSize.Y)
				{
					newPos.Y -= (minSize.Y - newSize.Y);
					newSize.Y = minSize.Y;
				}
				break;
				
			case ResizeEdge.BottomLeft:
				newPos.X += relative.X;
				newSize.X -= relative.X;
				newSize.Y += relative.Y;
				if (newSize.X < minSize.X)
				{
					newPos.X -= (minSize.X - newSize.X);
					newSize.X = minSize.X;
				}
				if (newSize.Y < minSize.Y)
					newSize.Y = minSize.Y;
				break;
				
			case ResizeEdge.BottomRight:
				newSize.X += relative.X;
				newSize.Y += relative.Y;
				if (newSize.X < minSize.X)
					newSize.X = minSize.X;
				if (newSize.Y < minSize.Y)
					newSize.Y = minSize.Y;
				break;
		}
		
		Position = newPos;
		Size = newSize;
	}
	
	private void SnapToNearestCorner()
	{
		if (_originalParent == null)
			return;
			
		var parentSize = _originalParent.Size;
		var center = GlobalPosition + Size / 2;
		
		// Determine which corner is closest
		float distBottomRight = center.DistanceTo(new Vector2(parentSize.X, parentSize.Y));
		float distBottomLeft = center.DistanceTo(new Vector2(0, parentSize.Y));
		float distTopRight = center.DistanceTo(new Vector2(parentSize.X, 0));
		float distTopLeft = center.DistanceTo(Vector2.Zero);
		
		float minDist = Mathf.Min(distBottomRight, Mathf.Min(distBottomLeft, Mathf.Min(distTopRight, distTopLeft)));
		
		Corner targetCorner = Corner.BottomRight;
		if (minDist == distBottomLeft)
			targetCorner = Corner.BottomLeft;
		else if (minDist == distTopRight)
			targetCorner = Corner.TopRight;
		else if (minDist == distTopLeft)
			targetCorner = Corner.TopLeft;
		
		_currentCorner = targetCorner;
		PositionAtCorner(_currentCorner);
	}
	
	private void PositionAtCorner(Corner corner)
	{
		if (_originalParent == null)
			return;
			
		var parentSize = _originalParent.Size;
		const float MARGIN = 10f;
		Vector2 newPosition = Vector2.Zero;
		
		switch (corner)
		{
			case Corner.BottomRight:
				newPosition = new Vector2(parentSize.X - Size.X - MARGIN, parentSize.Y - Size.Y - MARGIN);
				break;
			case Corner.BottomLeft:
				newPosition = new Vector2(MARGIN, parentSize.Y - Size.Y - MARGIN);
				break;
			case Corner.TopRight:
				newPosition = new Vector2(parentSize.X - Size.X - MARGIN, MARGIN);
				break;
			case Corner.TopLeft:
				newPosition = new Vector2(MARGIN, MARGIN);
				break;
		}
		
		Position = newPosition;
	}
	
	private void OnToggleModePressed()
	{
		if (_isInDedicatedWindow)
		{
			// Return to main viewport
			ReturnToMainViewport();
		}
		else
		{
			// Create dedicated window
			CreateDedicatedWindow();
		}
	}
	
	private void CreateDedicatedWindow()
	{
		if (_dedicatedWindow != null)
			return;
		
		// Ensure we have valid references
		var sceneTree = GetTree();
		var parent = GetParent();
		
		if (sceneTree == null || sceneTree.Root == null || parent == null)
		{
			GD.PrintErr("Cannot create dedicated window: Missing required references");
			return;
		}
			
		_dedicatedWindow = new Window();
		_dedicatedWindow.Title = "Preview Viewport";
		_dedicatedWindow.Size = new Vector2I(800, 600);
		_dedicatedWindow.CloseRequested += OnDedicatedWindowClosed;
		
		// Center the window on screen
		var screenSize = DisplayServer.ScreenGetSize();
		var windowSize = _dedicatedWindow.Size;
		_dedicatedWindow.Position = new Vector2I(
			(screenSize.X - windowSize.X) / 2,
			(screenSize.Y - windowSize.Y) / 2
		);
		
		// Remove from parent and add to dedicated window
		parent.RemoveChild(this);
		_dedicatedWindow.AddChild(this);
		
		// Update layout
		AnchorsPreset = (int)LayoutPreset.FullRect;
		OffsetLeft = 0;
		OffsetTop = 0;
		OffsetRight = 0;
		OffsetBottom = 0;
		
		// Add window to scene tree
		sceneTree.Root.AddChild(_dedicatedWindow);
		_dedicatedWindow.Show();
		
		_isInDedicatedWindow = true;
		if (ToggleModeButton != null)
			ToggleModeButton.Text = "Dock";
		
		// Disable dragging visual feedback
		if (HeaderBar != null)
		{
			HeaderBar.MouseDefaultCursorShape = CursorShape.Arrow;
		}
		
		// Hide resize border when in dedicated window
		if (ResizeBorder != null)
		{
			ResizeBorder.Visible = false;
		}
	}
	
	private void ReturnToMainViewport()
	{
		if (_dedicatedWindow == null || !IsInstanceValid(_dedicatedWindow))
			return;
			
		// Remove from dedicated window
		_dedicatedWindow.RemoveChild(this);
		
		// Add back to original parent
		if (_originalParent != null && IsInstanceValid(_originalParent))
		{
			// Reset anchors to not stretch
			AnchorsPreset = (int)LayoutPreset.TopLeft;
			
			// Set fixed size with minimum
			CustomMinimumSize = new Vector2(200, 150);
			Size = new Vector2(400, 300);
			
			_originalParent.AddChild(this);
			PositionAtCorner(_currentCorner);
		}
		
		_isInDedicatedWindow = false;
		if (ToggleModeButton != null)
			ToggleModeButton.Text = "Pop Out";
		
		// Re-enable dragging and resizing
		if (HeaderBar != null)
		{
			HeaderBar.MouseDefaultCursorShape = CursorShape.Move;
		}
		
		MouseFilter = MouseFilterEnum.Pass;
		
		// Show resize border when docked
		if (ResizeBorder != null)
		{
			ResizeBorder.Visible = true;
		}
		
		// Close and cleanup window
		_dedicatedWindow.QueueFree();
		_dedicatedWindow = null;
	}
	
	private void OnDedicatedWindowClosed()
	{
		ReturnToMainViewport();
	}
	
	public void SetMainCamera(Camera3D mainCamera)
	{
		if (mainCamera == null || PreviewCamera == null)
			return;
			
		// Copy transform from main camera
		PreviewCamera.GlobalTransform = mainCamera.GlobalTransform;
	}
	
	public void SyncWithMainCamera(Camera3D mainCamera, bool syncTransform = true)
	{
		if (mainCamera == null || PreviewCamera == null)
			return;
		
		_mainCamera = mainCamera;
			
		if (syncTransform)
		{
			PreviewCamera.GlobalTransform = mainCamera.GlobalTransform;
		}
		
		// Sync camera properties
		PreviewCamera.Fov = mainCamera.Fov;
		PreviewCamera.Near = mainCamera.Near;
		PreviewCamera.Far = mainCamera.Far;
		PreviewCamera.Projection = mainCamera.Projection;
	}
	
	public void SetMainViewport(SubViewport mainViewport)
	{
		_mainViewport = mainViewport;
		
		// Share the same World3D with the main viewport
		// This automatically shares all scene objects, lights, and environment
		if (_mainViewport != null && PreviewSubViewport != null)
		{
			PreviewSubViewport.World3D = _mainViewport.World3D;
		}
	}
	
	public override void _Process(double delta)
	{
		base._Process(delta);
		
		// Sync camera transform based on active camera
		if (PreviewCamera != null)
		{
			if (_useWorkCamera && _mainCamera != null && IsInstanceValid(_mainCamera))
			{
				// Use work camera
				PreviewCamera.GlobalTransform = _mainCamera.GlobalTransform;
				PreviewCamera.Fov = _mainCamera.Fov;
			}
			else if (!_useWorkCamera && _activeSceneCamera != null && IsInstanceValid(_activeSceneCamera))
			{
				// Use scene camera - teleport to its position
				PreviewCamera.GlobalTransform = _activeSceneCamera.GlobalTransform;
				PreviewCamera.Fov = _activeSceneCamera.Fov;
			}
		}
		
		// Update FPS counter if visible
		if (FpsLabel != null && FpsLabel.Visible)
		{
			int fps = (int)Engine.GetFramesPerSecond();
			FpsLabel.Text = $"FPS: {fps}";
		}
	}
	
	public void SetFpsLabelVisible(bool visible)
	{
		if (FpsLabel != null)
		{
			FpsLabel.Visible = visible;
		}
	}
	
	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
		{
			// Cleanup dedicated window if it exists
			if (_dedicatedWindow != null && IsInstanceValid(_dedicatedWindow))
			{
				_dedicatedWindow.QueueFree();
			}
		}
	}
	
	// Camera management methods
	public void OnCameraSpawned(CameraSceneObject camera)
	{
		if (camera == null || _sceneCameras.Contains(camera))
			return;
			
		_sceneCameras.Add(camera);
		RefreshCameraDropdown();
		GD.Print($"Camera '{camera.Name}' added to preview viewport");
	}
	
	public void OnCameraRemoved(CameraSceneObject camera)
	{
		if (camera == null || !_sceneCameras.Contains(camera))
			return;
			
		_sceneCameras.Remove(camera);
		
		// If the removed camera was active, switch back to work camera
		if (_activeSceneCamera == camera)
		{
			_activeSceneCamera = null;
			_useWorkCamera = true;
		}
		
		RefreshCameraDropdown();
		GD.Print($"Camera '{camera.Name}' removed from preview viewport");
	}
	
	private void RefreshCameraDropdown()
	{
		if (CameraDropdown == null)
			return;
			
		CameraDropdown.Clear();
		
		// Add work camera as first option
		CameraDropdown.AddItem("Work Camera", 0);
		
		// Add all scene cameras
		for (int i = 0; i < _sceneCameras.Count; i++)
		{
			var camera = _sceneCameras[i];
			if (IsInstanceValid(camera))
			{
				CameraDropdown.AddItem(camera.Name, i + 1);
			}
		}
		
		// Select the correct camera
		if (_useWorkCamera)
		{
			CameraDropdown.Selected = 0;
		}
		else if (_activeSceneCamera != null)
		{
			int index = _sceneCameras.IndexOf(_activeSceneCamera);
			if (index >= 0)
			{
				CameraDropdown.Selected = index + 1;
			}
		}
	}
	
	private void OnCameraDropdownChanged(long index)
	{
		if (index == 0)
		{
			// Switch to work camera
			_useWorkCamera = true;
			_activeSceneCamera = null;
			GD.Print("Switched to Work Camera");
		}
		else
		{
			// Switch to scene camera
			int cameraIndex = (int)index - 1;
			if (cameraIndex >= 0 && cameraIndex < _sceneCameras.Count)
			{
				_useWorkCamera = false;
				_activeSceneCamera = _sceneCameras[cameraIndex];
				GD.Print($"Switched to camera: {_activeSceneCamera.Name}");
			}
		}
	}
	
	public void RefreshCameraList()
	{
		// Scan the viewport for all camera scene objects
		_sceneCameras.Clear();
		
		if (_mainViewport != null)
		{
			ScanForCameras(_mainViewport);
		}
		
		RefreshCameraDropdown();
	}
	
	private void ScanForCameras(Node node)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is CameraSceneObject camera)
			{
				if (!_sceneCameras.Contains(camera))
				{
					_sceneCameras.Add(camera);
				}
			}
			
			// Recursively scan children
			if (child.GetChildCount() > 0)
			{
				ScanForCameras(child);
			}
		}
	}
	
	/// <summary>
	/// Renders a single image at the specified resolution
	/// </summary>
	public async void RenderImage(string filePath, string format, int width, int height)
	{
		if (PreviewSubViewport == null)
		{
			GD.PrintErr("Cannot render: PreviewSubViewport is null");
			return;
		}
		
		// Store original size and update mode
		var originalSize = PreviewSubViewport.Size;
		var originalUpdateMode = PreviewSubViewport.RenderTargetUpdateMode;
		ViewportContainer.Stretch = false;
		
		// Disable selection material overlays during rendering
		var selectedObjects = SelectionManager.Instance?.SelectedObjects ?? new Godot.Collections.Array<SceneObject>();
		foreach (var obj in selectedObjects)
		{
			obj.ApplySelectionMaterial(false);
		}
		
		// Ensure viewport is actively rendering
		PreviewSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		
		// Set render resolution - only change viewport size, keep container as-is
		PreviewSubViewport.Size = new Vector2I(width, height);
		
		// Wait for viewport to update and render
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		
		// Get the rendered image
		var image = PreviewSubViewport.GetTexture().GetImage();
		
		// Save the image
		Error saveResult = Error.Failed;
		switch (format.ToUpper())
		{
			case "PNG":
				saveResult = image.SavePng(filePath);
				break;
			case "JPG":
			case "JPEG":
				saveResult = image.SaveJpg(filePath);
				break;
			case "WEBP":
				saveResult = image.SaveWebp(filePath);
				break;
			case "BMP":
				saveResult = image.SavePng(filePath); // Godot doesn't have SaveBmp, use PNG
				break;
		}
		
		// Restore original size and update mode
		PreviewSubViewport.Size = originalSize;
		PreviewSubViewport.RenderTargetUpdateMode = originalUpdateMode;
		ViewportContainer.Stretch = true;
		
		// Re-enable selection material overlays
		foreach (var obj in selectedObjects)
		{
			obj.ApplySelectionMaterial(true);
		}
		
		if (saveResult == Error.Ok)
		{
			GD.Print($"Image rendered successfully to: {filePath}");
		}
		else
		{
			GD.PrintErr($"Failed to save image to: {filePath}");
		}
	}
	
	/// <summary>
	/// Renders an animation as either a video file or PNG sequence
	/// </summary>
	public async void RenderAnimation(string outputPath, string format, bool isPngSequence, int bitrateMbps, int width, int height, float framerate, int lastFrame)
	{
		if (PreviewSubViewport == null)
		{
			GD.PrintErr("Cannot render: PreviewSubViewport is null");
			return;
		}
		
		if (lastFrame <= 0)
		{
			GD.PrintErr("Cannot render: No keyframes found in animation");
			return;
		}
		
		// Store original size and update mode
		var originalSize = PreviewSubViewport.Size;
		var originalUpdateMode = PreviewSubViewport.RenderTargetUpdateMode;
		ViewportContainer.Stretch = false;
		
		// Disable selection material overlays during rendering
		var selectedObjects = SelectionManager.Instance?.SelectedObjects ?? new Godot.Collections.Array<SceneObject>();
		foreach (var obj in selectedObjects)
		{
			obj.ApplySelectionMaterial(false);
		}
		
		// Ensure viewport is actively rendering
		PreviewSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		
		// Set render resolution - only change viewport size, keep container as-is
		PreviewSubViewport.Size = new Vector2I(width, height);
		
		GD.Print($"Starting animation render: {lastFrame} frames at {framerate} FPS");
		
		if (isPngSequence)
		{
			// Render PNG sequence
			await RenderPngSequence(outputPath, lastFrame, framerate);
		}
		else
		{
			// Render video file
			await RenderVideoFile(outputPath, format, bitrateMbps, lastFrame, framerate);
		}
		
		// Restore original size and update mode
		PreviewSubViewport.Size = originalSize;
		PreviewSubViewport.RenderTargetUpdateMode = originalUpdateMode;
		ViewportContainer.Stretch = true;
		
		// Re-enable selection material overlays
		foreach (var obj in selectedObjects)
		{
			obj.ApplySelectionMaterial(true);
		}
		
		GD.Print("Animation render complete");
	}
	
	private async System.Threading.Tasks.Task RenderPngSequence(string outputDirectory, int lastFrame, float framerate)
	{
		var timeline = TimelinePanel.Instance;
		if (timeline == null)
		{
			GD.PrintErr("Cannot render: TimelinePanel instance is null");
			return;
		}
		
		// Create output directory if it doesn't exist
		DirAccess.MakeDirRecursiveAbsolute(outputDirectory);
		
		for (int frame = 0; frame <= lastFrame; frame++)
		{
			// Set timeline to current frame
			timeline.SetCurrentFrame(frame);
			
			// Wait for frame to update and render
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
			
			// Capture frame
			var image = PreviewSubViewport.GetTexture().GetImage();
			var framePath = System.IO.Path.Combine(outputDirectory, $"frame_{frame:D5}.png");
			var saveResult = image.SavePng(framePath);
			
			if (saveResult != Error.Ok)
			{
				GD.PrintErr($"Failed to save frame {frame} to: {framePath}");
			}
			
			// Progress feedback
			if (frame % 10 == 0)
			{
				GD.Print($"Rendered frame {frame}/{lastFrame}");
			}
		}
		
		GD.Print($"PNG sequence saved to: {outputDirectory}");
	}
	
	private async System.Threading.Tasks.Task RenderVideoFile(string outputPath, string format, int bitrateMbps, int lastFrame, float framerate)
	{
		var timeline = TimelinePanel.Instance;
		if (timeline == null)
		{
			GD.PrintErr("Cannot render: TimelinePanel instance is null");
			return;
		}
		
		// Create temporary directory for frames
		var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"render_{System.Guid.NewGuid()}");
		DirAccess.MakeDirRecursiveAbsolute(tempDir);
		
		GD.Print($"Rendering frames to temporary directory: {tempDir}");
		
		// Render all frames to temp directory
		for (int frame = 0; frame <= lastFrame; frame++)
		{
			// Set timeline to current frame
			timeline.SetCurrentFrame(frame);
			
			// Wait for frame to update and render
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
			
			// Capture frame
			var image = PreviewSubViewport.GetTexture().GetImage();
			var framePath = System.IO.Path.Combine(tempDir, $"frame_{frame:D5}.png");
			var saveResult = image.SavePng(framePath);
			
			if (saveResult != Error.Ok)
			{
				GD.PrintErr($"Failed to save frame {frame} to: {framePath}");
			}
			
			// Progress feedback
			if (frame % 10 == 0)
			{
				GD.Print($"Rendered frame {frame}/{lastFrame}");
			}
		}
		
		// Use FFMpegCore to encode video
		GD.Print("Encoding video with FFMpegCore...");
		
		try
		{
			// Get the first frame to determine video dimensions
			var firstFramePath = System.IO.Path.Combine(tempDir, "frame_00000.png");
			if (!System.IO.File.Exists(firstFramePath))
			{
				GD.PrintErr("First frame not found for video encoding");
				return;
			}
			
			// Use FFMpegCore to convert image sequence to video
			var success = FFMpegArguments
				.FromFileInput(System.IO.Path.Combine(tempDir, "frame_%05d.png"), false, options => options
					.WithFramerate(framerate))
				.OutputToFile(outputPath, true, options => options
					.WithVideoCodec(VideoCodec.LibX264)
					.WithVideoBitrate(bitrateMbps * 1000) // Convert Mbps to Kbps
					.WithConstantRateFactor(21)
					.WithFastStart())
				.ProcessSynchronously();
			
			if (success)
			{
				GD.Print($"Video encoded successfully to: {outputPath}");
			}
			else
			{
				GD.PrintErr("FFMpeg encoding failed");
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Error encoding video: {ex.Message}");
			GD.PrintErr($"Stack trace: {ex.StackTrace}");
		}
		
		// Clean up temporary directory
		try
		{
			System.IO.Directory.Delete(tempDir, true);
			GD.Print("Temporary files cleaned up");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Failed to clean up temporary directory: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Sets the render resolution for the preview viewport
	/// </summary>
	public void SetRenderResolution(int width, int height)
	{
		if (PreviewSubViewport != null)
		{
			PreviewSubViewport.Size = new Vector2I(width, height);
		}
	}
}
