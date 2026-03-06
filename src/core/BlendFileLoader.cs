using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Handles loading Blender .blend files by exporting them to GLB via Blender's CLI,
/// then converting Blender materials to Godot ShaderMaterial instances.
/// </summary>
public class BlendFileLoader
{
	private static BlendFileLoader _instance;
	public static BlendFileLoader Instance => _instance ??= new BlendFileLoader();

	// Cached shader instance – shared across all material conversions so the GPU
	// only needs to compile it once.  Lazily initialised on first use.
	private static Shader _cachedBlenderShader;

	// Cache of exported GLB paths keyed by blend file path + modification time
	private readonly Dictionary<string, string> _exportCache = new();

	// Cache of material sidecar data keyed by GLB path
	private readonly Dictionary<string, BlenderMaterialFile> _materialCache = new();

	// Cache of generated per-material shaders keyed by material name + blend file
	private readonly Dictionary<string, Shader> _generatedShaderCache = new();

	// Temp directory for exported GLBs
	private static readonly string TempExportDir = Path.Combine(
		System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
		"SimplyRemadeNuxi", "BlendExports");

	public BlendFileLoader()
	{
		// Ensure temp directory exists
		try
		{
			Directory.CreateDirectory(TempExportDir);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"BlendFileLoader: Could not create temp directory: {ex.Message}");
		}
	}

	/// <summary>
	/// Finds the newest Blender executable on the system.
	/// Collects all candidates from PATH and common install locations,
	/// then returns the one with the highest version number.
	/// </summary>
	public static string FindBlenderExecutable()
	{
		var candidates = new List<(string path, Version version)>();

		void AddCandidate(string exePath)
		{
			if (!File.Exists(exePath)) return;
			var ver = GetBlenderVersion(exePath);
			candidates.Add((exePath, ver));
		}

		// Check PATH
		var pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
		foreach (var dir in pathEnv.Split(Path.PathSeparator))
		{
			AddCandidate(Path.Combine(dir, "blender.exe"));
			AddCandidate(Path.Combine(dir, "blender"));
		}

		// Check common Windows install locations
		var programFiles = new[]
		{
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),
		};

		foreach (var pf in programFiles)
		{
			if (string.IsNullOrEmpty(pf)) continue;

			// Blender Foundation\Blender X.Y\ layout
			var blenderFoundation = Path.Combine(pf, "Blender Foundation");
			if (Directory.Exists(blenderFoundation))
			{
				foreach (var subDir in Directory.GetDirectories(blenderFoundation))
				{
					AddCandidate(Path.Combine(subDir, "blender.exe"));
				}
			}

			// Direct Program Files\Blender\blender.exe
			AddCandidate(Path.Combine(pf, "Blender", "blender.exe"));
		}

		// Steam install
		AddCandidate(Path.Combine(
			System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),
			"Steam", "steamapps", "common", "Blender", "blender.exe"));

		if (candidates.Count == 0)
			return null;

		// Sort by version descending and return the newest
		candidates.Sort((a, b) => b.version.CompareTo(a.version));
		var best = candidates[0];
		GD.Print($"BlendFileLoader: Using Blender {best.version} at '{best.path}'");
		return best.path;
	}

	/// <summary>
	/// Attempts to determine the Blender version from the executable path.
	/// Falls back to Version(0,0) if it cannot be determined.
	/// </summary>
	private static Version GetBlenderVersion(string exePath)
	{
		// Try to parse version from directory name (e.g., "Blender 4.2", "Blender 5.0")
		var dir = Path.GetDirectoryName(exePath) ?? "";
		var dirName = Path.GetFileName(dir);

		// Match patterns like "Blender 4.2", "Blender 5.0.1"
		var match = System.Text.RegularExpressions.Regex.Match(
			dirName, @"(\d+)\.(\d+)(?:\.(\d+))?");
		if (match.Success)
		{
			int major = int.Parse(match.Groups[1].Value);
			int minor = int.Parse(match.Groups[2].Value);
			int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
			return new Version(major, minor, patch);
		}

		// Try running blender --version to get the version
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = exePath,
				Arguments = "--version",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				CreateNoWindow = true
			};
			using var proc = Process.Start(psi);
			if (proc != null)
			{
				var output = proc.StandardOutput.ReadToEnd();
				proc.WaitForExit(3000);
				var vMatch = System.Text.RegularExpressions.Regex.Match(
					output, @"Blender\s+(\d+)\.(\d+)(?:\.(\d+))?");
				if (vMatch.Success)
				{
					int major = int.Parse(vMatch.Groups[1].Value);
					int minor = int.Parse(vMatch.Groups[2].Value);
					int patch = vMatch.Groups[3].Success ? int.Parse(vMatch.Groups[3].Value) : 0;
					return new Version(major, minor, patch);
				}
			}
		}
		catch { /* ignore */ }

		return new Version(0, 0);
	}

	/// <summary>
	/// Exports a .blend file to a temporary .glb file using Blender's CLI.
	/// Returns the path to the exported GLB, or null on failure.
	/// </summary>
	public string ExportBlendToGlb(string blendFilePath)
	{
		if (!File.Exists(blendFilePath))
		{
			GD.PrintErr($"BlendFileLoader: .blend file not found: {blendFilePath}");
			return null;
		}

		var blenderExe = FindBlenderExecutable();
		if (string.IsNullOrEmpty(blenderExe))
		{
			GD.PrintErr("BlendFileLoader: Blender executable not found. Please install Blender and ensure it is in your PATH.");
			return null;
		}

		// Build a cache key from path + last write time + export script version
		// Increment the version number when the export script changes to force re-export
		const string exportScriptVersion = "v18";
		var lastWrite = File.GetLastWriteTimeUtc(blendFilePath).Ticks.ToString();
		var cacheKey = blendFilePath + "|" + lastWrite + "|" + exportScriptVersion;

		if (_exportCache.TryGetValue(cacheKey, out var cachedGlb) && File.Exists(cachedGlb))
		{
			GD.Print($"BlendFileLoader: Using cached GLB: {cachedGlb}");

			// Also load the material sidecar if it hasn't been loaded yet
			if (!_materialCache.ContainsKey(cachedGlb))
			{
				var cachedSidecar = Path.ChangeExtension(cachedGlb, ".materials.json");
				if (File.Exists(cachedSidecar))
				{
					var matFile = LoadMaterialSidecar(cachedSidecar);
					if (matFile != null)
						_materialCache[cachedGlb] = matFile;
				}
			}

			return cachedGlb;
		}

		// Generate output path
		var baseName = Path.GetFileNameWithoutExtension(blendFilePath);
		var outputGlb = Path.Combine(TempExportDir, $"{baseName}_{lastWrite}.glb");

		// Write the Python export script to a temp file and run it with --python
		var scriptPath = WritePythonExportScript(outputGlb);

		GD.Print($"BlendFileLoader: Exporting '{blendFilePath}' to '{outputGlb}' via Blender...");

		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = blenderExe,
				Arguments = $"--background \"{blendFilePath}\" --python \"{scriptPath}\"",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using var process = Process.Start(psi);
			if (process == null)
			{
				GD.PrintErr("BlendFileLoader: Failed to start Blender process.");
				return null;
			}

			var stdout = process.StandardOutput.ReadToEnd();
			var stderr = process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (!string.IsNullOrWhiteSpace(stdout))
				GD.Print($"BlendFileLoader [Blender stdout]: {stdout}");
			if (!string.IsNullOrWhiteSpace(stderr))
				GD.Print($"BlendFileLoader [Blender stderr]: {stderr}");

			if (process.ExitCode != 0)
			{
				GD.PrintErr($"BlendFileLoader: Blender exited with code {process.ExitCode}");
				return null;
			}

			if (!File.Exists(outputGlb))
			{
				GD.PrintErr($"BlendFileLoader: Blender ran but output GLB was not created: {outputGlb}");
				return null;
			}

			GD.Print($"BlendFileLoader: Export successful -> {outputGlb}");
				_exportCache[cacheKey] = outputGlb;
	
				// Load the material sidecar JSON if it was produced
				var sidecarPath = Path.ChangeExtension(outputGlb, ".materials.json");
				if (File.Exists(sidecarPath))
				{
					var matFile = LoadMaterialSidecar(sidecarPath);
					if (matFile != null)
						_materialCache[outputGlb] = matFile;
				}
	
				return outputGlb;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"BlendFileLoader: Exception during Blender export: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Loads a .blend file: exports it to GLB and loads the GLB scene.
	/// Returns the root Node3D, or null on failure.
	/// Call ConvertMaterialsInHierarchy() after adding the node to the scene tree
	/// so that all texture resources are fully resolved.
	/// </summary>
	public Node3D LoadBlendFile(string blendFilePath)
	{
		var glbPath = ExportBlendToGlb(blendFilePath);
		if (string.IsNullOrEmpty(glbPath))
			return null;

		// Load the GLB using Godot's GLTF pipeline
		var gltfDocument = new GltfDocument();
		var gltfState = new GltfState();

		var error = gltfDocument.AppendFromFile(glbPath, gltfState);
		if (error != Error.Ok)
		{
			GD.PrintErr($"BlendFileLoader: Failed to load exported GLB '{glbPath}': {error}");
			return null;
		}

		var root = gltfDocument.GenerateScene(gltfState);
		if (root == null)
		{
			GD.PrintErr("BlendFileLoader: Failed to generate scene from exported GLB.");
			return null;
		}

		if (root is not Node3D root3D)
		{
			GD.PrintErr($"BlendFileLoader: GLB root is not Node3D, got {root.GetType().Name}");
			root.QueueFree();
			return null;
		}

		// NOTE: Material conversion (ConvertMaterialsInHierarchy) must be called
		// AFTER the node is added to the scene tree so texture resources are fully resolved.
		return root3D;
	}

	/// <summary>
	/// Recursively walks the node hierarchy and converts every StandardMaterial3D
	/// found on MeshInstance3D surfaces to a Blender-compatible ShaderMaterial.
	/// Optionally uses the material sidecar from the given GLB path to generate
	/// node-graph-based shaders.
	/// </summary>
	public void ConvertMaterialsInHierarchy(Node node, string glbPath = null)
	{
		BlenderMaterialFile matFile = null;
		if (glbPath != null)
			_materialCache.TryGetValue(glbPath, out matFile);

		ConvertMaterialsInHierarchyInternal(node, matFile, glbPath);
	}

	/// <summary>
	/// Convenience overload: looks up the exported GLB path for the given .blend file
	/// and uses the associated material sidecar to generate node-graph-based shaders.
	/// Call this instead of <see cref="ConvertMaterialsInHierarchy(Node, string)"/> when
	/// you have the original .blend file path but not the exported GLB path.
	/// </summary>
	public void ConvertMaterialsForBlendFile(Node node, string blendFilePath)
	{
		// Find the cached GLB path for this blend file
		string glbPath = null;
		foreach (var kv in _exportCache)
		{
			if (kv.Key.StartsWith(blendFilePath + "|"))
			{
				glbPath = kv.Value;
				break;
			}
		}

		ConvertMaterialsInHierarchy(node, glbPath);
	}

	private void ConvertMaterialsInHierarchyInternal(Node node, BlenderMaterialFile matFile, string glbPath)
	{
		if (node is MeshInstance3D meshInstance)
		{
			ConvertMeshInstanceMaterials(meshInstance, matFile, glbPath);
		}

		foreach (var child in node.GetChildren())
		{
			ConvertMaterialsInHierarchyInternal(child, matFile, glbPath);
		}
	}

	/// <summary>
	/// Converts all surface materials on a MeshInstance3D from StandardMaterial3D
	/// to a ShaderMaterial.  When a material sidecar is available the shader is
	/// generated from the Blender node graph; otherwise falls back to the static
	/// Blender PBR shader.
	/// </summary>
	private void ConvertMeshInstanceMaterials(MeshInstance3D meshInstance,
		BlenderMaterialFile matFile, string glbPath)
	{
		if (meshInstance.Mesh == null) return;

		int surfaceCount = meshInstance.Mesh.GetSurfaceCount();
		for (int i = 0; i < surfaceCount; i++)
		{
			// Check surface override first, then mesh surface material
			var mat = meshInstance.GetSurfaceOverrideMaterial(i)
			          ?? meshInstance.Mesh.SurfaceGetMaterial(i);

			if (mat is StandardMaterial3D stdMat)
			{
				GD.Print($"BlendFileLoader: Converting material '{stdMat.ResourceName}' on '{meshInstance.Name}' surface {i}" +
				         $" | AlbedoTex={stdMat.AlbedoTexture != null}" +
				         $" | MetallicTex={stdMat.MetallicTexture != null}" +
				         $" | RoughnessTex={stdMat.RoughnessTexture != null}" +
				         $" | NormalTex={stdMat.NormalTexture != null}");

				ShaderMaterial shaderMat;

				// Try to use node-graph-based shader generation
				if (matFile != null && !string.IsNullOrEmpty(stdMat.ResourceName) &&
				    matFile.Materials.TryGetValue(stdMat.ResourceName, out var blenderMat))
				{
					shaderMat = CreateNodeGraphShaderMaterial(blenderMat, stdMat, glbPath);
					GD.Print($"BlendFileLoader: Generated node-graph shader for '{stdMat.ResourceName}'");
				}
				else
				{
					// Fallback to static PBR shader
					shaderMat = ConvertStandardToShaderMaterial(stdMat);
				}

				shaderMat.RenderPriority = -1;

				meshInstance.SetSurfaceOverrideMaterial(i, shaderMat);
			}
			else if (mat != null)
			{
				GD.Print($"BlendFileLoader: Surface {i} on '{meshInstance.Name}' has non-StandardMaterial3D: {mat.GetType().Name}");
			}
		}
	}

	/// <summary>
	/// Creates a ShaderMaterial from a Blender node graph, binding textures from
	/// the StandardMaterial3D that was imported from the GLB.
	/// </summary>
	private ShaderMaterial CreateNodeGraphShaderMaterial(
		BlenderMaterialInfo blenderMat,
		StandardMaterial3D stdMat,
		string glbPath)
	{
		// Collect texture names referenced by TEX_IMAGE nodes
		var textureNames = new List<string>();
		foreach (var node in blenderMat.Nodes)
		{
			if (node.Type == "TEX_IMAGE" && !string.IsNullOrEmpty(node.ImageName))
			{
				if (!textureNames.Contains(node.ImageName))
					textureNames.Add(node.ImageName);
			}
		}

		// Generate or retrieve cached shader
		var cacheKey = $"{glbPath}|{blenderMat.Name}";
		if (!_generatedShaderCache.TryGetValue(cacheKey, out var shader))
		{
			var shaderSource = BlenderShaderGenerator.Generate(blenderMat, textureNames);
			GD.Print($"BlendFileLoader: Generated shader for '{blenderMat.Name}':\n{shaderSource}");

			shader = new Shader();
			shader.Code = shaderSource;
			_generatedShaderCache[cacheKey] = shader;
		}

		var shaderMat = new ShaderMaterial();
		shaderMat.Shader = shader;

		// Bind textures: map image names to the textures available in the StandardMaterial3D.
		// The GLTF importer puts the albedo texture in AlbedoTexture, normal in NormalTexture, etc.
		// We use a best-effort heuristic to match image names to texture slots.
		for (int ti = 0; ti < textureNames.Count; ti++)
		{
			var imgName = textureNames[ti];
			var uniformName = $"tex_{ti}";

			// Find the TEX_IMAGE node for this image to understand its role
			var texNode = blenderMat.Nodes.FirstOrDefault(n =>
				n.Type == "TEX_IMAGE" && n.ImageName == imgName);

			Texture2D tex = ResolveTextureForNode(texNode, blenderMat, stdMat);
			if (tex != null)
				shaderMat.SetShaderParameter(uniformName, tex);
		}

		return shaderMat;
	}

	/// <summary>
	/// Attempts to resolve which Godot texture corresponds to a Blender TEX_IMAGE node
	/// by examining what socket the node's Color output is connected to.
	/// Walks the link graph transitively to find the eventual BSDF socket.
	/// </summary>
	private static Texture2D ResolveTextureForNode(
		BlenderNode texNode,
		BlenderMaterialInfo blenderMat,
		StandardMaterial3D stdMat)
	{
		if (texNode == null) return stdMat.AlbedoTexture;

		// BFS/DFS through outgoing links to find the eventual BSDF socket
		var visited = new HashSet<string>();
		var queue = new Queue<(string nodeName, string fromSocket)>();
		queue.Enqueue((texNode.Name, "Color"));

		while (queue.Count > 0)
		{
			var (currentNode, currentSocket) = queue.Dequeue();
			if (!visited.Add(currentNode + "." + currentSocket)) continue;

			foreach (var link in blenderMat.Links)
			{
				if (link.FromNode != currentNode) continue;
				// Only follow links from the relevant socket (Color or Alpha)
				if (link.FromSocket != currentSocket && link.FromSocket != "Color" && link.FromSocket != "Alpha") continue;

				var destNode = blenderMat.Nodes.FirstOrDefault(n => n.Name == link.ToNode);
				if (destNode == null) continue;

				// Direct connection to Principled BSDF
				if (destNode.Type == "BSDF_PRINCIPLED")
				{
					return link.ToSocket switch
					{
						"Base Color"                   => stdMat.AlbedoTexture,
						"Metallic"                     => stdMat.MetallicTexture ?? stdMat.AlbedoTexture,
						"Roughness"                    => stdMat.RoughnessTexture ?? stdMat.AlbedoTexture,
						"Normal"                       => stdMat.NormalTexture ?? stdMat.AlbedoTexture,
						"Emission" or "Emission Color" => stdMat.EmissionTexture ?? stdMat.AlbedoTexture,
						_                              => stdMat.AlbedoTexture
					};
				}

				// Connection via Normal Map node → normal texture
				if (destNode.Type == "NORMAL_MAP")
					return stdMat.NormalTexture ?? stdMat.AlbedoTexture;

				// Connection via Bump node → normal texture
				if (destNode.Type == "BUMP")
					return stdMat.NormalTexture ?? stdMat.AlbedoTexture;

				// Intermediate node (MIX_RGB, MATH, etc.) – follow its outputs
				queue.Enqueue((destNode.Name, link.ToSocket));
			}
		}

		// Fallback: use colour space hint
		if (texNode.ColorSpace is "Non-Color" or "Linear" or "Raw")
			return stdMat.MetallicTexture ?? stdMat.RoughnessTexture ?? stdMat.AlbedoTexture;

		// Final fallback: always return the albedo texture so the sampler is bound
		return stdMat.AlbedoTexture;
	}

	/// <summary>
	/// Loads and parses the material sidecar JSON file produced by the Python export script.
	/// </summary>
	private static BlenderMaterialFile LoadMaterialSidecar(string sidecarPath)
	{
		try
		{
			var json = File.ReadAllText(sidecarPath);
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var matFile = JsonSerializer.Deserialize<BlenderMaterialFile>(json, options);
			GD.Print($"BlendFileLoader: Loaded material sidecar with {matFile?.Materials.Count ?? 0} materials.");
			return matFile;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"BlendFileLoader: Failed to load material sidecar '{sidecarPath}': {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Returns the shared Blender PBR shader, loading it from the .gdshader resource
	/// file when first called so Godot can cache and pre-compile it properly.
	/// Falls back to creating an inline Shader if the resource is not found.
	/// </summary>
	private static Shader GetOrCreateBlenderShader()
	{
		if (_cachedBlenderShader != null)
			return _cachedBlenderShader;

		// Prefer the pre-existing .gdshader resource so Godot's shader cache can
		// warm it up before the first GLB is rendered.
		const string shaderResPath = "res://assets/Shaders/Blender.gdshader";
		if (ResourceLoader.Exists(shaderResPath))
		{
			_cachedBlenderShader = ResourceLoader.Load<Shader>(shaderResPath);
			if (_cachedBlenderShader != null)
			{
				GD.Print("BlendFileLoader: Loaded Blender shader from resource file.");
				return _cachedBlenderShader;
			}
		}
		
		return _cachedBlenderShader;
	}

	/// <summary>
	/// Pre-warms the Blender PBR shader by creating a dummy ShaderMaterial that
	/// references it.  Call this early (e.g. on app startup) so the GPU driver
	/// compiles the shader before the first GLB is loaded, preventing a first-load
	/// crash caused by synchronous shader compilation during rendering.
	/// </summary>
	public static void PreWarmShader()
	{
		var shader = GetOrCreateBlenderShader();
		// Creating a ShaderMaterial that references the shader is enough to
		// trigger Godot's background shader compilation pipeline.
		var dummy = new ShaderMaterial { Shader = shader };
		// Immediately discard – we only needed the compilation side-effect.
		dummy.Dispose();
		GD.Print("BlendFileLoader: Blender PBR shader pre-warm requested.");
	}

	/// <summary>
	/// Converts a StandardMaterial3D (as imported from GLTF/GLB) into a ShaderMaterial
	/// that uses the shared Blender PBR-compatible shader.
	/// </summary>
	public ShaderMaterial ConvertStandardToShaderMaterial(StandardMaterial3D stdMat)
	{
		var shaderMat = new ShaderMaterial();
		shaderMat.Shader = GetOrCreateBlenderShader();
		shaderMat.RenderPriority = -1;

		// --- Albedo ---
		shaderMat.SetShaderParameter("albedo_color", stdMat.AlbedoColor);
		if (stdMat.AlbedoTexture != null)
		{
			shaderMat.SetShaderParameter("albedo_texture", stdMat.AlbedoTexture);
			shaderMat.SetShaderParameter("use_albedo_texture", true);
		}

		// --- Metallic ---
		shaderMat.SetShaderParameter("metallic", stdMat.Metallic);
		if (stdMat.MetallicTexture != null)
		{
			shaderMat.SetShaderParameter("metallic_texture", stdMat.MetallicTexture);
			shaderMat.SetShaderParameter("use_metallic_texture", true);
		}

		// --- Roughness ---
		shaderMat.SetShaderParameter("roughness", stdMat.Roughness);
		if (stdMat.RoughnessTexture != null)
		{
			shaderMat.SetShaderParameter("roughness_texture", stdMat.RoughnessTexture);
			shaderMat.SetShaderParameter("use_roughness_texture", true);
		}

		// --- Normal map ---
		if (stdMat.NormalTexture != null)
		{
			shaderMat.SetShaderParameter("normal_texture", stdMat.NormalTexture);
			shaderMat.SetShaderParameter("use_normal_texture", true);
			shaderMat.SetShaderParameter("normal_scale", stdMat.NormalScale);
		}

		// --- Emission ---
		if (stdMat.EmissionEnabled)
		{
			shaderMat.SetShaderParameter("emission_color", new Vector3(
				stdMat.Emission.R, stdMat.Emission.G, stdMat.Emission.B));
			shaderMat.SetShaderParameter("emission_energy", stdMat.EmissionEnergyMultiplier);
			if (stdMat.EmissionTexture != null)
			{
				shaderMat.SetShaderParameter("emission_texture", stdMat.EmissionTexture);
				shaderMat.SetShaderParameter("use_emission_texture", true);
			}
		}

		// --- Alpha / transparency ---
		if (stdMat.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
		{
			shaderMat.SetShaderParameter("use_alpha_scissor", true);
			shaderMat.SetShaderParameter("alpha_scissor_threshold", stdMat.AlphaScissorThreshold);
		}

		return shaderMat;
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Writes a Python export script to a temp file and returns its path.
	/// The script handles Blender 3.x/4.x/5.x differences and rewires
	/// custom material node trees so image textures are exported via GLTF.
	/// </summary>
	private static string WritePythonExportScript(string outputGlbPath)
	{
		// Escape backslashes for Python string literal
		var escapedPath = outputGlbPath.Replace("\\", "\\\\");
		// Sidecar JSON path (same base name, .materials.json extension)
		var sidecarPath = Path.ChangeExtension(outputGlbPath, ".materials.json")
			.Replace("\\", "\\\\");

		var script = $@"import bpy
import os
import json

print('Blender version:', bpy.app.version_string)

# Pack all external images into the blend data so they are embedded in the GLB
bpy.ops.file.pack_all()

def ungroup_material(mat):
	   """"""Ungroup all GROUP nodes in a material's node tree so the GLTF exporter
	   can see the Image Texture nodes directly.""""""
	   if not mat.node_tree:
	       return
	   nt = mat.node_tree
	   max_iterations = 20
	   for _ in range(max_iterations):
	       group_nodes = [n for n in nt.nodes if n.type == 'GROUP']
	       if not group_nodes:
	           break
	       try:
	           for group_node in list(group_nodes):
	               if group_node.node_tree is None:
	                   nt.nodes.remove(group_node)
	                   continue
	               inner_nt = group_node.node_tree
	               node_map = {{}}
	               for inner_node in inner_nt.nodes:
	                   if inner_node.type in ('GROUP_INPUT', 'GROUP_OUTPUT'):
	                       continue
	                   new_node = nt.nodes.new(inner_node.bl_idname)
	                   new_node.location = (group_node.location.x + inner_node.location.x,
	                                        group_node.location.y + inner_node.location.y)
	                   if inner_node.type == 'TEX_IMAGE':
	                       new_node.image = inner_node.image
	                   node_map[inner_node.name] = new_node
	               # Recreate internal links
	               for link in inner_nt.links:
	                   fn = link.from_node.name
	                   tn = link.to_node.name
	                   if fn in node_map and tn in node_map:
	                       try:
	                           nt.links.new(node_map[fn].outputs[link.from_socket.name],
	                                        node_map[tn].inputs[link.to_socket.name])
	                       except Exception:
	                           pass
	               # Connect group outputs to downstream nodes
	               for out_sock in group_node.outputs:
	                   if out_sock.is_linked:
	                       go_node = next((n for n in inner_nt.nodes if n.type == 'GROUP_OUTPUT'), None)
	                       if go_node:
	                           inner_sock = go_node.inputs.get(out_sock.name)
	                           if inner_sock and inner_sock.is_linked:
	                               src_inner = inner_sock.links[0].from_node.name
	                               src_sock_name = inner_sock.links[0].from_socket.name
	                               if src_inner in node_map:
	                                   for dest_link in out_sock.links:
	                                       try:
	                                           nt.links.new(node_map[src_inner].outputs[src_sock_name],
	                                                        dest_link.to_socket)
	                                       except Exception:
	                                           pass
	               # Connect group inputs from upstream nodes
	               for in_sock in group_node.inputs:
	                   if in_sock.is_linked:
	                       gi_node = next((n for n in inner_nt.nodes if n.type == 'GROUP_INPUT'), None)
	                       if gi_node:
	                           inner_out = gi_node.outputs.get(in_sock.name)
	                           if inner_out and inner_out.is_linked:
	                               for dest_link in inner_out.links:
	                                   dest_inner = dest_link.to_node.name
	                                   dest_sock_name = dest_link.to_socket.name
	                                   if dest_inner in node_map:
	                                       try:
	                                           nt.links.new(in_sock.links[0].from_socket,
	                                                        node_map[dest_inner].inputs[dest_sock_name])
	                                       except Exception:
	                                           pass
	               nt.nodes.remove(group_node)
	       except Exception as e:
	           print('Ungroup error for ' + mat.name + ': ' + str(e))
	           break

def socket_default_value(sock):
	   """"""Return the default value of a socket as a list of floats.""""""
	   try:
	       dv = sock.default_value
	       if hasattr(dv, '__iter__'):
	           return list(dv)
	       else:
	           return [float(dv)]
	   except Exception:
	       return [0.0]

def dump_material_nodes(mat):
	   """"""Serialise the node tree of a material to a dict.""""""
	   if not mat.node_tree:
	       return None
	   nt = mat.node_tree
	   nodes_data = []
	   for node in nt.nodes:
	       nd = {{
	           'name': node.name,
	           'type': node.type,
	           'inputs': {{}}
	       }}
	       # Extra per-type data
	       if node.type == 'TEX_IMAGE':
	           nd['image_name'] = node.image.name if node.image else None
	           try:
	               nd['color_space'] = node.image.colorspace_settings.name if node.image else None
	           except Exception:
	               nd['color_space'] = None
	       if hasattr(node, 'operation'):
	           nd['operation'] = node.operation
	       if hasattr(node, 'space'):
	           nd['space'] = node.space
	       if hasattr(node, 'vector_type'):
	           nd['vector_type'] = node.vector_type
	       if node.type == 'VALUE':
	           try:
	               nd['value'] = float(node.outputs[0].default_value)
	           except Exception:
	               nd['value'] = 0.0
	       if node.type == 'RGB':
	           try:
	               nd['color'] = list(node.outputs[0].default_value)
	           except Exception:
	               nd['color'] = [0.5, 0.5, 0.5, 1.0]
	       # Capture unlinked input default values
	       for inp in node.inputs:
	           if not inp.is_linked:
	               nd['inputs'][inp.name] = socket_default_value(inp)
	       nodes_data.append(nd)

	   links_data = []
	   for link in nt.links:
	       links_data.append({{
	           'from_node': link.from_node.name,
	           'from_socket': link.from_socket.name,
	           'to_node': link.to_node.name,
	           'to_socket': link.to_socket.name,
	       }})

	   return {{
	       'name': mat.name,
	       'use_nodes': mat.use_nodes,
	       'blend_mode': getattr(mat, 'blend_method', 'OPAQUE'),
	       'alpha_threshold': getattr(mat, 'alpha_threshold', 0.5),
	       'use_backface_culling': getattr(mat, 'use_backface_culling', True),
	       'nodes': nodes_data,
	       'links': links_data,
	   }}

def find_albedo_image(mat):
	   """"""After ungrouping, find the Image Texture node that feeds the Base Color
	   of the Principled BSDF (or the first Image Texture node as fallback).
	   Returns the bpy.types.Image, or None.""""""
	   nt = mat.node_tree
	   if not nt:
	       return None
	   # Prefer the Image Texture connected to Base Color of a Principled BSDF
	   for node in nt.nodes:
	       if node.type == 'BSDF_PRINCIPLED':
	           bc = node.inputs.get('Base Color')
	           if bc and bc.is_linked:
	               src = bc.links[0].from_node
	               if src.type == 'TEX_IMAGE' and src.image:
	                   return src.image
	   # Fallback: first Image Texture node with an image
	   for node in nt.nodes:
	       if node.type == 'TEX_IMAGE' and node.image:
	           return node.image
	   return None

def rebuild_material_with_image(mat, img):
	   """"""Replace the material's node tree with a minimal Principled BSDF
	   wired to the given image texture.""""""
	   nt = mat.node_tree
	   nt.nodes.clear()
	   tex_node = nt.nodes.new('ShaderNodeTexImage')
	   tex_node.image = img
	   tex_node.location = (-300, 0)
	   pbsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
	   pbsdf.location = (0, 0)
	   pbsdf.inputs['Roughness'].default_value = 1.0
	   pbsdf.inputs['Metallic'].default_value = 0.0
	   alpha_in = pbsdf.inputs.get('Alpha')
	   if alpha_in:
	       alpha_in.default_value = 1.0
	   out_node = nt.nodes.new('ShaderNodeOutputMaterial')
	   out_node.location = (300, 0)
	   nt.links.new(tex_node.outputs['Color'], pbsdf.inputs['Base Color'])
	   nt.links.new(pbsdf.outputs['BSDF'], out_node.inputs['Surface'])
	   print('Rebuilt ' + mat.name + ' with image: ' + img.name)

# --- Step 1: Ungroup all materials ---
processed = set()
for mat in bpy.data.materials:
	   if mat.name in processed or not mat.node_tree:
	       continue
	   processed.add(mat.name)
	   ungroup_material(mat)

# --- Step 2: Dump node graphs to JSON sidecar (after ungrouping, before rebuilding) ---
mat_sidecar = {{'materials': {{}}}}
for mat in bpy.data.materials:
	   if not mat.node_tree:
	       continue
	   mat_data = dump_material_nodes(mat)
	   if mat_data:
	       mat_sidecar['materials'][mat.name] = mat_data

sidecar_path = r'{sidecarPath}'
try:
	   with open(sidecar_path, 'w', encoding='utf-8') as f:
	       json.dump(mat_sidecar, f, indent=2)
	   print('Wrote material sidecar: ' + sidecar_path)
	   print('  Materials dumped: ' + str(len(mat_sidecar['materials'])))
except Exception as e:
	   print('Failed to write material sidecar: ' + str(e))

# --- Step 3: Rebuild materials for GLTF export (simple Principled BSDF + image) ---
for mat in bpy.data.materials:
	   if not mat.node_tree:
	       continue
	   img = find_albedo_image(mat)
	   if img:
	       rebuild_material_with_image(mat, img)
	   else:
	       print('No image texture found for material: ' + mat.name + ' - leaving as-is')

# Remove all unused node groups
for ng in list(bpy.data.node_groups):
	   if ng.users == 0:
	       ng_name = ng.name
	       bpy.data.node_groups.remove(ng)
	       print('Removed unused node group: ' + ng_name)

# Build version-safe export kwargs
ver = bpy.app.version
kwargs = dict(
	   filepath=r'{escapedPath}',
	   export_format='GLB',
	   export_materials='EXPORT',
	   export_image_format='AUTO',
	   export_texcoords=True,
	   export_normals=True,
	   export_tangents=True,
	   export_cameras=False,
	   export_lights=False,
	   use_selection=False,
	   export_apply=True,
)

# export_colors was removed in Blender 4.x
if ver < (4, 0, 0):
	   kwargs['export_colors'] = True

result = bpy.ops.export_scene.gltf(**kwargs)
print('Export result:', result)
";

		var scriptPath = Path.Combine(TempExportDir, "blend_export.py");
		File.WriteAllText(scriptPath, script);
		return scriptPath;
	}

}
