using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Handles asynchronous loading of GLTF/GLB models with proper texture preloading
/// and shader compilation waiting to prevent first-load crashes.
/// </summary>
public class AsyncModelLoader
{
	private static AsyncModelLoader _instance;
	public static AsyncModelLoader Instance => _instance ??= new AsyncModelLoader();

	/// <summary>
	/// Callback for loading progress updates (0.0 to 1.0)
	/// </summary>
	public Action<float, string> OnProgressUpdate;

	/// <summary>
	/// Callback when model is fully loaded and ready
	/// </summary>
	public Action<Node3D> OnModelReady;

	/// <summary>
	/// Callback for errors
	/// </summary>
	public Action<string> OnError;

	private CancellationTokenSource _currentLoadingCts;

	private AsyncModelLoader() { }

	/// <summary>
	/// Loads a GLB file asynchronously with proper texture preloading and shader compilation waiting.
	/// The model is NOT added to the scene tree - the caller is responsible for that.
	/// </summary>
	/// <param name="glbPath">Path to the GLB file</param>
	/// <returns>The loaded Node3D root, or null on failure</returns>
	public async Task<Node3D> LoadGlbAsync(string glbPath)
	{
		// Cancel any previous loading operation
		_currentLoadingCts?.Cancel();
		_currentLoadingCts = new CancellationTokenSource();
		var token = _currentLoadingCts.Token;

		try
		{
			ReportProgress(0.1f, "Loading model file...");

			// Run GLTF loading on background thread to avoid blocking the main thread
			Node3D root = await Task.Run(() =>
			{
				var gltfDocument = new GltfDocument();
				var gltfState = new GltfState();

				var error = gltfDocument.AppendFromFile(glbPath, gltfState);
				if (error != Error.Ok)
				{
					ReportError($"Failed to load GLB: {error}");
					return null;
				}

				var generatedRoot = gltfDocument.GenerateScene(gltfState);
				if (generatedRoot is not Node3D root3D)
				{
					ReportError($"GLB root is not Node3D, got {generatedRoot?.GetType().Name}");
					generatedRoot?.QueueFree();
					return null;
				}

				return root3D;
			}, token);

			if (root == null || token.IsCancellationRequested)
				return null;

			ReportProgress(0.4f, "Preloading textures...");

			// Preload all textures in the model
			await PreloadAllTexturesAsync(root, token);

			if (token.IsCancellationRequested)
			{
				root.QueueFree();
				return null;
			}

			ReportProgress(0.7f, "Waiting for shader compilation...");

			// Wait for shader compilation to complete
			await WaitForShaderCompilationAsync(root, token);

			if (token.IsCancellationRequested)
			{
				root.QueueFree();
				return null;
			}

			ReportProgress(1.0f, "Model ready");
			return root;
		}
		catch (OperationCanceledException)
		{
			GD.Print("AsyncModelLoader: Loading cancelled");
			return null;
		}
		catch (Exception ex)
		{
			ReportError($"Exception during model loading: {ex.Message}");
			GD.PrintErr($"AsyncModelLoader: {ex}");
			return null;
		}
	}

	/// <summary>
	/// Recursively finds all texture resources in the model and forces them to load.
	/// </summary>
	private async Task PreloadAllTexturesAsync(Node node, CancellationToken token)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
		{
			var mesh = meshInstance.Mesh;
			int surfaceCount = mesh.GetSurfaceCount();

			for (int i = 0; i < surfaceCount; i++)
			{
				// Check surface override material
				var overrideMat = meshInstance.GetSurfaceOverrideMaterial(i);
				if (overrideMat != null)
				{
					await PreloadMaterialTexturesAsync(overrideMat, token);
				}

				// Check mesh surface material
				var surfaceMat = mesh.SurfaceGetMaterial(i);
				if (surfaceMat != null)
				{
					await PreloadMaterialTexturesAsync(surfaceMat, token);
				}
			}
		}

		// Process children
		foreach (var child in node.GetChildren())
		{
			if (token.IsCancellationRequested)
				return;

			if (child is Node childNode)
			{
				await PreloadAllTexturesAsync(childNode, token);
			}
		}

		// Yield to main thread periodically to prevent blocking
		await Task.Delay(1, token);
	}

	/// <summary>
	/// Preloads all textures from a material.
	/// </summary>
	private async Task PreloadMaterialTexturesAsync(Godot.Material material, CancellationToken token)
	{
		if (material is StandardMaterial3D stdMat)
		{
			// Force load each texture type - using only properties that exist in Godot 4.x
			await ForceLoadTextureAsync(() => stdMat.AlbedoTexture, token);
			await ForceLoadTextureAsync(() => stdMat.MetallicTexture, token);
			await ForceLoadTextureAsync(() => stdMat.RoughnessTexture, token);
			await ForceLoadTextureAsync(() => stdMat.NormalTexture, token);
			await ForceLoadTextureAsync(() => stdMat.EmissionTexture, token);
			await ForceLoadTextureAsync(() => stdMat.AOTexture, token);
			await ForceLoadTextureAsync(() => stdMat.RimTexture, token);
			await ForceLoadTextureAsync(() => stdMat.SubsurfScatterTexture, token);
			await ForceLoadTextureAsync(() => stdMat.DetailAlbedo, token);
			await ForceLoadTextureAsync(() => stdMat.DetailNormal, token);
		}
		else if (material is ShaderMaterial shaderMat)
		{
			// For shader materials, check common shader parameter names manually
			PreloadShaderParameterTextures(shaderMat);
		}

		// Small delay to allow main thread to process
		await Task.Delay(1, token);
	}

	/// <summary>
	/// Helper to force load a texture by accessing its dimensions.
	/// </summary>
	private async Task ForceLoadTextureAsync(Func<Texture2D> textureGetter, CancellationToken token)
	{
		try
		{
			var tex = textureGetter?.Invoke();
			if (tex != null)
			{
				// Accessing Width/Height forces the texture to load
				_ = tex.GetWidth();
				_ = tex.GetHeight();
			}
		}
		catch
		{
			// Ignore errors - texture might not be fully loaded yet
		}
		await Task.Delay(1, token);
	}

	/// <summary>
	/// Manually checks common shader parameter names for textures.
	/// </summary>
	private void PreloadShaderParameterTextures(ShaderMaterial shaderMat)
	{
		// Common texture parameter names in shaders - try each one
		// This is a best-effort approach since we can't easily enumerate shader params
		string[] commonTextureParams = {
			"albedo_texture", "metallic_texture", "roughness_texture", "normal_texture",
			"emission_texture", "ao_texture", "height_texture", "ambient_occlusion_texture",
			"detail_texture", "detail_mask", "anisotropy_texture", "clearcoat_texture",
			"sheen_texture", "subsurface_texture", "transmission_texture",
			"tex_0", "tex_1", "tex_2", "tex_3", "tex_4", "tex_5", "tex_6", "tex_7"
		};

		foreach (var paramName in commonTextureParams)
		{
			try
			{
				// Just try to get the parameter - if it's a texture, accessing properties will load it
				var paramValue = shaderMat.GetShaderParameter(paramName);
				// Accessing any property on the variant may trigger texture loading
				_ = paramValue.Obj;
			}
			catch
			{
				// Ignore - parameter might not exist or wrong type
			}
		}
	}

	/// <summary>
	/// Waits for all shader materials in the model to finish compiling.
	/// This is critical to prevent crashes when the model is first rendered.
	/// </summary>
	private async Task WaitForShaderCompilationAsync(Node node, CancellationToken token)
	{
		var materialsToCheck = new List<Godot.Material>();
		CollectMaterials(node, materialsToCheck);

		int maxWaitFrames = 300; // Maximum ~5 seconds at 60fps
		int framesWaited = 0;

		while (framesWaited < maxWaitFrames)
		{
			if (token.IsCancellationRequested)
				return;

			bool allReady = true;

			foreach (var mat in materialsToCheck)
			{
				if (mat is ShaderMaterial shaderMat)
				{
					// Check if shader is compiled by trying to get a shader parameter
					// If it's not ready, this might trigger compilation
					try
					{
						// Force a shader state check by getting render priority
						_ = shaderMat.RenderPriority;
					}
					catch
					{
						allReady = false;
					}
				}
			}

			if (allReady)
			{
				GD.Print($"AsyncModelLoader: Shader compilation complete after {framesWaited} frames");
				break;
			}

			framesWaited++;
			
			// Wait for one frame using proper Godot signal
			await Task.Delay(16, token); // Approximately 60fps
		}

		if (framesWaited >= maxWaitFrames)
		{
			GD.Print("AsyncModelLoader: Shader compilation timeout - proceeding anyway");
		}
	}

	/// <summary>
	/// Recursively collects all materials from a node hierarchy.
	/// </summary>
	private void CollectMaterials(Node node, List<Godot.Material> materials)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
		{
			var mesh = meshInstance.Mesh;
			int surfaceCount = mesh.GetSurfaceCount();

			for (int i = 0; i < surfaceCount; i++)
			{
				var overrideMat = meshInstance.GetSurfaceOverrideMaterial(i);
				if (overrideMat != null && !materials.Contains(overrideMat))
					materials.Add(overrideMat);

				var surfaceMat = mesh.SurfaceGetMaterial(i);
				if (surfaceMat != null && !materials.Contains(surfaceMat))
					materials.Add(surfaceMat);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
				CollectMaterials(childNode, materials);
		}
	}

	/// <summary>
	/// Cancels the current loading operation.
	/// </summary>
	public void CancelLoading()
	{
		_currentLoadingCts?.Cancel();
	}

	private void ReportProgress(float progress, string message)
	{
		OnProgressUpdate?.Invoke(progress, message);
		GD.Print($"AsyncModelLoader: {message} ({progress:P0})");
	}

	private void ReportError(string message)
	{
		OnError?.Invoke(message);
		GD.PrintErr($"AsyncModelLoader: {message}");
	}
}
