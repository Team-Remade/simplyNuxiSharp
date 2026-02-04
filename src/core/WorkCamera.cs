using Godot;
using Godot.Collections;

namespace simplyRemadeNuxi.core;

public partial class WorkCamera : Camera3D
{
	private float MoveSpeed = 5.0f;
	private float FastMoveSpeed = 15.0f;
	private float MinMoveSpeed = 0.5f;
	private float MaxMoveSpeed = 50.0f;
	private float MouseSensitivity = 0.005f;
	private float OrbitSensitivity = 0.005f;
	private float ZoomSpeed = 0.5f;
	private float MinDistance = 1.0f;
	private float MaxDistance = 50.0f;
	private float OrbitDeadzone = 5.0f;
	
	private bool IsFlying = false;
	private bool IsDragging = false;
	
	private Vector3 OrbitTarget = Vector3.Zero;
	private float OrbitDistance = 10.0f;
	private float OrbitYaw = 0.0f;
	private float OrbitPitch = -0.5f;
	
	private Vector2 OrbitClickPosition = Vector2.Zero;
	
	private SubViewport PickViewport;
	private Control ViewportContainer;
	private SubViewport SubViewport;
	[Export] private Shader PickingShader;

	public override void _Ready()
	{
		OrbitTarget = Vector3.Zero;
		OrbitDistance = GlobalPosition.Length();
		
		//Calculate yaw and pitch from position
		var flatPos = new Vector3(GlobalPosition.X, 0, GlobalPosition.Z);
		if (flatPos.Length() > 0.001)
		{
			OrbitYaw = Mathf.Atan2(flatPos.X, flatPos.Z);
		}

		OrbitPitch = Mathf.Atan2(GlobalPosition.Y, flatPos.Length());
		
		UpdateOrbitTransform();

		SubViewport = GetParent<SubViewport>();
		ViewportContainer = GetParent().GetParent<Control>();
		SetupPicking();
	}

	private void SetupPicking()
	{
		PickViewport = new SubViewport();
		PickViewport.Name = "PickViewport";
		PickViewport.HandleInputLocally = false;
		//Only render when picking
		PickViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		PickViewport.Size = SubViewport.Size;
		PickViewport.RenderTargetClearMode = SubViewport.ClearMode.Always;
		PickViewport.TransparentBg = true;
		PickViewport.DebugDraw = Viewport.DebugDrawEnum.Unshaded;
		
		var pickCam = new Camera3D();
		pickCam.Name = "PickCam";
		pickCam.GlobalTransform = GlobalTransform;
		pickCam.Fov = Fov;
		pickCam.CullMask = 0x7FFFFFFF;
		PickViewport.AddChild(pickCam);
		
		SubViewport.CallDeferred("add_child", PickViewport);
		SubViewport.CallDeferred("move_child", PickViewport, -1);
		
		PickViewport.World3D = SubViewport.World3D;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouseEvent)
		{
			SetFlying(mouseEvent.Pressed);
		}

		// Check if gizmo is being hovered or edited
		bool gizmoInteracting = SelectionManager.Instance?.Gizmo != null && 
				(SelectionManager.Instance.Gizmo.Hovering || SelectionManager.Instance.Gizmo.Editing);

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } buttonEvent)
		{
			if (buttonEvent.Pressed)
			{
				IsDragging = false;
				OrbitClickPosition = buttonEvent.Position;
			}
			else
			{
				if (!IsDragging && !IsFlying && !gizmoInteracting)
				{
					HandleSelection(GetViewport().GetMousePosition());
				}

				IsDragging = false;
				if (!IsFlying)
				{
					Input.SetMouseMode(Input.MouseModeEnum.Visible);
				}
			}
		}

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown} eventMouseButton)
		{
			int zoomDir = 0;
			switch (eventMouseButton.ButtonIndex)
			{
				case MouseButton.WheelUp:
					zoomDir = -1;
					break;
				case MouseButton.WheelDown:
					zoomDir = 1;
					break;
			}

			if (IsFlying)
			{
				MoveSpeed = Mathf.Clamp(MoveSpeed - zoomDir * 0.5f, MinMoveSpeed, MaxMoveSpeed);
			}
			else
			{
				OrbitDistance = Mathf.Clamp(OrbitDistance - ZoomSpeed * zoomDir, MinMoveSpeed, MaxMoveSpeed);
				UpdateOrbitTransform();
			}
		}

		if (@event is InputEventMouseMotion motionEvent)
		{
			if (IsFlying)
			{
				var rot = Rotation;
				rot.Y -= motionEvent.Relative.X * MouseSensitivity;
				rot.X -= motionEvent.Relative.Y * MouseSensitivity;
				rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(-90), Mathf.DegToRad(90));
				Rotation = rot;
			}
			else if (Input.IsMouseButtonPressed(MouseButton.Left) && !gizmoInteracting)
			{
				if (!IsDragging)
				{
					var delta = motionEvent.Position - OrbitClickPosition;
					if (delta.Length() > OrbitDeadzone)
					{
						IsDragging = true;
						Input.SetMouseMode(Input.MouseModeEnum.Captured);
					}
				}
				else
				{
					OrbitYaw -= motionEvent.Relative.X * OrbitSensitivity;
					OrbitPitch += motionEvent.Relative.Y * OrbitSensitivity;
					OrbitPitch = Mathf.Clamp(OrbitPitch, Mathf.DegToRad(-89), Mathf.DegToRad(89));
					UpdateOrbitTransform();
				}
			}
		}
	}

	private async void HandleSelection(Vector2 mousePosition)
	{
		if (PickViewport == null || PickViewport.Size.X == 0 || PickViewport.Size.Y == 0) return;
		
		var originalUpdateMode = SubViewport.RenderTargetUpdateMode;
		SubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		
		PickViewport.ProcessMode = ProcessModeEnum.Inherit;
		PickViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		
		var pickX = Mathf.FloorToInt(mousePosition.X);
		var pickY = Mathf.FloorToInt(mousePosition.Y);
		
		var sceneObjects = GetTree().GetNodesInGroup("SceneObject");

		foreach (var sceneObject in sceneObjects)
		{
			if (sceneObject is SceneObject { IsSelectable: true } so)
			{
				UpdateObjectForPicking(so);
			}
		}
		
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);

		var img = PickViewport.GetTexture().GetImage();
		if (img != null)
		{
			var pix = img.GetPixel(pickX, pickY);
			HandlePickColor(pix, sceneObjects);
		}

		foreach (var sceneObject in sceneObjects)
		{
			if (sceneObject is SceneObject { IsSelectable: true } so)
			{
				RestoreObjectMaterial(so);
			}
		}
		
		PickViewport.ProcessMode = ProcessModeEnum.Disabled;
		PickViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		
		SubViewport.RenderTargetUpdateMode = originalUpdateMode;
	}

	private void UpdateObjectForPicking(SceneObject sceneObject)
	{
		var meshes = sceneObject.GetMeshInstancesRecursively(sceneObject.Visual);
		foreach (var mesh in meshes)
		{
			var mat = new ShaderMaterial();
			mat.Shader = PickingShader;
			mat.SetShaderParameter("pick_color", sceneObject.PickColor);
			
			mesh.MaterialOverride = mat;
		}

		foreach (var child in sceneObject.GetChildren())
		{
			if (child is SceneObject so)
			{
				UpdateObjectForPicking(so);
			}
		}
	}

	private void RestoreObjectMaterial(SceneObject sceneObject)
	{
		var meshes = sceneObject.GetMeshInstancesRecursively(sceneObject.Visual);
		foreach (var mesh in meshes)
		{
			mesh.MaterialOverride = null;
		}

		foreach (var child in sceneObject.GetChildren())
		{
			if (child is SceneObject so)
			{
				RestoreObjectMaterial(so);
			}
		}
	}

	private void HandlePickColor(Color color, Array<Node> sceneObjects)
	{
		bool isCtrlPressed = Input.IsKeyPressed(Key.Ctrl);
		
		if (color.A < 0.5f)
		{
			//Empty space - clear selection unless Ctrl is pressed
			if (!isCtrlPressed)
			{
				SelectionManager.Instance.ClearSelection();
			}
			return;
		}

		foreach (var sceneObject in sceneObjects)
		{
			if (sceneObject is SceneObject { IsSelectable: true } so)
			{
				if (color == so.PickColor)
				{
					if (isCtrlPressed)
					{
						// Ctrl+Click toggles selection (multiselect)
						SelectionManager.Instance.ToggleSelection(so);
					}
					else
					{
						// Normal click clears selection and selects new object
						SelectionManager.Instance.ClearSelection();
						SelectionManager.Instance.SelectObject(so);
					}
					return;
				}
			}
		}
	}

	public override void _Process(double delta)
	{
		SyncPickCamera();

		if (PickViewport != null && SubViewport != null)
		{
			if (PickViewport.Size != SubViewport.Size)
			{
				PickViewport.Size = SubViewport.Size;
			}
		}
		
		if (!IsFlying) return;

		var speed = Input.IsKeyPressed(Key.Shift) ? FastMoveSpeed : MoveSpeed;
		var direction = Vector3.Zero;

		if (Input.IsKeyPressed(Key.W))
		{
			direction -= Transform.Basis.Z;
		}
		
		if (Input.IsKeyPressed(Key.S))
		{
			direction += Transform.Basis.Z;
		} 
		
		if (Input.IsKeyPressed(Key.A))
		{
			direction -= Transform.Basis.X;
		} 
		
		if (Input.IsKeyPressed(Key.D))
		{
			direction += Transform.Basis.X;
		}

		if (Input.IsKeyPressed(Key.E))
		{
			direction += Vector3.Up;
		}

		if (Input.IsKeyPressed(Key.Q))
		{
			direction -= Vector3.Up;
		}
		
		GlobalPosition += direction.Normalized() * speed * (float)delta;
		
		OrbitTarget += direction.Normalized() * speed * (float)delta;
	}

	private void SyncPickCamera()
	{
		if (PickViewport == null) return;
		
		var pickCam = PickViewport.GetNodeOrNull<Camera3D>("PickCam");
		if (pickCam != null)
		{
			pickCam.GlobalTransform = GlobalTransform;
			pickCam.Fov = Fov;
		}
	}

	private void UpdateOrbitTransform()
	{
		var y = OrbitDistance * Mathf.Sin(OrbitPitch);
		var r = OrbitDistance * Mathf.Cos(OrbitPitch);
		var x = r * Mathf.Sin(OrbitYaw);
		var z = r * Mathf.Cos(OrbitYaw);
		
		GlobalPosition = OrbitTarget + new Vector3(x, y, z);
		LookAt(OrbitTarget, Vector3.Up);
	}

	private void SetFlying(bool state)
	{
		IsFlying = state;

		Input.SetMouseMode(state ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible);
	}
}