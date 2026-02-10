using Godot;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Represents a camera object in the scene. Unlike real Godot cameras,
/// this is just a Node3D marker that the viewport camera can teleport to.
/// </summary>
public partial class CameraSceneObject : SceneObject
{
	// Camera properties
	public float Fov { get; set; } = 75.0f;
	public float Near { get; set; } = 0.05f;
	public float Far { get; set; } = 4000.0f;
	
	public CameraSceneObject()
	{
		ObjectType = "Camera";
		
		// Load the camera GLB visual
		CreateCameraVisual();
	}
	
	private void CreateCameraVisual()
	{
		// Load the Camera.glb file
		var gltfDocument = new GltfDocument();
		var gltfState = new GltfState();
		
		string glbPath = "res://assets/mesh/Camera.glb";
		var error = gltfDocument.AppendFromFile(glbPath, gltfState);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to load Camera.glb: {error}");
			return;
		}
		
		// Generate the scene from GLTF
		var cameraNode = gltfDocument.GenerateScene(gltfState);
		
		if (cameraNode == null)
		{
			GD.PrintErr("Failed to generate scene from Camera.glb");
			return;
		}
		
		// Cast to Node3D
		if (cameraNode is not Node3D cameraNode3D)
		{
			GD.PrintErr($"Camera.glb root is not a Node3D, it's a {cameraNode.GetType().Name}");
			cameraNode.QueueFree();
			return;
		}
		
		// Set only cull layer 2 on all mesh instances in the GLB
		SetCullLayerToLayer2Only(cameraNode3D);
		
		AddVisualInstance(cameraNode3D);
		GD.Print("Camera visual loaded from Camera.glb");
	}
	
	private void SetCullLayerToLayer2Only(Node node)
	{
		// Recursively set cull layer to only layer 2 (disable layer 1, enable layer 2)
		if (node is VisualInstance3D visualInstance)
		{
			// Set to only render on layer 2 (bit 1, since layers are 0-indexed)
			// This disables layer 1 and enables only layer 2
			visualInstance.Layers = (uint)(1 << 1); // Only layer 2 enabled
		}
		
		foreach (var child in node.GetChildren())
		{
			SetCullLayerToLayer2Only(child);
		}
	}
	
	public new string GetObjectIcon()
	{
		return "Camera";
	}
}
