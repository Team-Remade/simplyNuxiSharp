using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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

	// Temp directory for exported GLBs
	private static readonly string TempExportDir = Path.Combine(
		System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
		"SimplyRemadeNuxi", "BlendExports");

	// Blender shader code for PBR material conversion
	private const string BlenderPbrShaderCode = @"
shader_type spatial;

uniform vec4 albedo_color : source_color = vec4(0.8, 0.8, 0.8, 1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white;
uniform bool use_albedo_texture = false;

uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D metallic_texture : hint_default_white;
uniform bool use_metallic_texture = false;

uniform float roughness : hint_range(0.0, 1.0) = 0.5;
uniform sampler2D roughness_texture : hint_default_white;
uniform bool use_roughness_texture = false;

uniform sampler2D normal_texture : hint_normal;
uniform bool use_normal_texture = false;
uniform float normal_scale : hint_range(-16.0, 16.0) = 1.0;

uniform sampler2D emission_texture : source_color, hint_default_black;
uniform bool use_emission_texture = false;
uniform vec3 emission_color : source_color = vec3(0.0, 0.0, 0.0);
uniform float emission_energy : hint_range(0.0, 16.0) = 1.0;

uniform float alpha_scissor_threshold : hint_range(0.0, 1.0) = 0.5;
uniform bool use_alpha_scissor = false;

void fragment() {
	vec4 base_color = albedo_color;
	if (use_albedo_texture) {
		base_color *= texture(albedo_texture, UV);
	}

	if (use_alpha_scissor && base_color.a < alpha_scissor_threshold) {
		discard;
	}

	ALBEDO = base_color.rgb;
	// Default to fully opaque; only use texture alpha if alpha scissor is enabled
	ALPHA = use_alpha_scissor ? base_color.a : 1.0;

	float metal = metallic;
	if (use_metallic_texture) {
		metal *= texture(metallic_texture, UV).r;
	}
	METALLIC = metal;

	float rough = roughness;
	if (use_roughness_texture) {
		rough *= texture(roughness_texture, UV).g;
	}
	ROUGHNESS = rough;

	if (use_normal_texture) {
		vec3 n = texture(normal_texture, UV).rgb;
		NORMAL_MAP = n;
		NORMAL_MAP_DEPTH = normal_scale;
	}

	vec3 emit = emission_color * emission_energy;
	if (use_emission_texture) {
		emit += texture(emission_texture, UV).rgb * emission_energy;
	}
	EMISSION = emit;
}
";

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
		const string exportScriptVersion = "v12";
		var lastWrite = File.GetLastWriteTimeUtc(blendFilePath).Ticks.ToString();
		var cacheKey = blendFilePath + "|" + lastWrite + "|" + exportScriptVersion;

		if (_exportCache.TryGetValue(cacheKey, out var cachedGlb) && File.Exists(cachedGlb))
		{
			GD.Print($"BlendFileLoader: Using cached GLB: {cachedGlb}");
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
	/// </summary>
	public void ConvertMaterialsInHierarchy(Node node)
	{
		if (node is MeshInstance3D meshInstance)
		{
			ConvertMeshInstanceMaterials(meshInstance);
		}

		foreach (var child in node.GetChildren())
		{
			ConvertMaterialsInHierarchy(child);
		}
	}

	/// <summary>
	/// Converts all surface materials on a MeshInstance3D from StandardMaterial3D
	/// to a ShaderMaterial that replicates Blender's Principled BSDF behaviour.
	/// </summary>
	private void ConvertMeshInstanceMaterials(MeshInstance3D meshInstance)
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
				var shaderMat = ConvertStandardToShaderMaterial(stdMat);
				meshInstance.SetSurfaceOverrideMaterial(i, shaderMat);
			}
			else if (mat != null)
			{
				GD.Print($"BlendFileLoader: Surface {i} on '{meshInstance.Name}' has non-StandardMaterial3D: {mat.GetType().Name}");
			}
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

		// Fallback: create inline (same behaviour as before, but still cached)
		GD.PrintErr("BlendFileLoader: Could not load Blender.gdshader resource, falling back to inline shader.");
		_cachedBlenderShader = new Shader { Code = BlenderPbrShaderCode };
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

		var script = $@"import bpy
import os
import tempfile

print('Blender version:', bpy.app.version_string)

# Pack all external images into the blend data
bpy.ops.file.pack_all()

BAKE_RESOLUTION = 1024  # Texture resolution for baked materials

def bake_material_to_texture(obj, mat, mat_slot_idx, bake_dir):
		  """"""Bake a material's diffuse/combined output to a new image texture.
		  Returns the new ImageTexture node, or None on failure.""""""
		  nt = mat.node_tree
		  if not nt:
		      return None

		  # Create a new image for baking
		  img_name = 'baked_' + mat.name.replace('/', '_').replace('\\\\', '_')
		  bake_img = bpy.data.images.new(img_name, width=BAKE_RESOLUTION, height=BAKE_RESOLUTION, alpha=True)
		  bake_img.colorspace_settings.name = 'sRGB'

		  # Add a temporary Image Texture node to receive the bake
		  bake_node = nt.nodes.new('ShaderNodeTexImage')
		  bake_node.image = bake_img
		  bake_node.name = '__bake_target__'

		  # Select the bake node as active (required by Blender's bake system)
		  for n in nt.nodes:
		      n.select = False
		  bake_node.select = True
		  nt.nodes.active = bake_node

		  # Set up bake settings
		  bpy.context.scene.render.engine = 'CYCLES'
		  bpy.context.scene.cycles.bake_type = 'DIFFUSE'
		  bpy.context.scene.render.bake.use_pass_direct = False
		  bpy.context.scene.render.bake.use_pass_indirect = False
		  bpy.context.scene.render.bake.use_pass_color = True
		  bpy.context.scene.render.bake.use_selected_to_active = False
		  bpy.context.scene.cycles.samples = 1

		  # Select only this object and make it active
		  bpy.ops.object.select_all(action='DESELECT')
		  obj.select_set(True)
		  bpy.context.view_layer.objects.active = obj

		  # Set the material slot
		  obj.active_material_index = mat_slot_idx

		  try:
		      bpy.ops.object.bake(type='DIFFUSE')
		      print('Baked ' + mat.name + ' -> ' + img_name)
		  except Exception as e:
		      print('Bake failed for ' + mat.name + ': ' + str(e))
		      nt.nodes.remove(bake_node)
		      bpy.data.images.remove(bake_img)
		      return None

		  # Save the baked image to a temp PNG file on disk, then pack it
		  # (Blender requires the image to be saved to disk before it can be packed)
		  import tempfile, os
		  tmp_path = os.path.join(tempfile.gettempdir(), img_name + '.png')
		  bake_img.filepath_raw = tmp_path
		  bake_img.file_format = 'PNG'
		  bake_img.save()
		  print('Saved baked image to: ' + tmp_path)
		  # Now pack it so it gets embedded in the GLB
		  bake_img.pack()
		  print('Packed baked image: ' + img_name + ' (is_dirty=' + str(bake_img.is_dirty) + ', packed=' + str(bake_img.packed_file is not None) + ')')

		  return bake_node, bake_img

def replace_material_with_baked(mat, bake_node, bake_img):
		  """"""Replace the material's node tree with a simple Principled BSDF + baked texture.""""""
		  nt = mat.node_tree
		  # Clear all nodes except the bake node
		  for n in list(nt.nodes):
		      if n != bake_node:
		          nt.nodes.remove(n)

		  # Rename the bake node
		  bake_node.name = 'Image Texture'

		  # Create Principled BSDF
		  pbsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
		  pbsdf.location = (300, 0)

		  # Create Material Output
		  out_node = nt.nodes.new('ShaderNodeOutputMaterial')
		  out_node.location = (600, 0)

		  # Wire: Image Texture -> Base Color -> BSDF -> Output
		  nt.links.new(bake_node.outputs['Color'], pbsdf.inputs['Base Color'])
		  # Do NOT connect alpha - baked diffuse images may have alpha=0
		  # Set alpha to 1.0 (fully opaque)
		  alpha_input = pbsdf.inputs.get('Alpha')
		  if alpha_input:
		      alpha_input.default_value = 1.0
		  nt.links.new(pbsdf.outputs['BSDF'], out_node.inputs['Surface'])

		  # Set roughness to 1 (no specular for baked diffuse)
		  pbsdf.inputs['Roughness'].default_value = 1.0
		  pbsdf.inputs['Metallic'].default_value = 0.0

		  print('Replaced ' + mat.name + ' with baked Principled BSDF')

def ungroup_material(mat):
		  """"""Ungroup all GROUP nodes in a material's node tree so the GLTF exporter
		  can see the Image Texture nodes directly.""""""
		  if not mat.node_tree:
		      return
		  nt = mat.node_tree
		  # Keep ungrouping until no GROUP nodes remain
		  max_iterations = 20
		  for _ in range(max_iterations):
		      group_nodes = [n for n in nt.nodes if n.type == 'GROUP']
		      if not group_nodes:
		          break
		      # Select only group nodes and ungroup them
		      for n in nt.nodes:
		          n.select = (n.type == 'GROUP')
		      # Use the override context to run the ungroup operator
		      try:
		          with bpy.context.temp_override(area=None):
		              # Manually ungroup by copying nodes from the group into the parent tree
		              for group_node in list(group_nodes):
		                  if group_node.node_tree is None:
		                      continue
		                  inner_nt = group_node.node_tree
		                  # Map from inner node to new outer node
		                  node_map = {{}}
		                  for inner_node in inner_nt.nodes:
		                      if inner_node.type in ('GROUP_INPUT', 'GROUP_OUTPUT'):
		                          continue
		                      new_node = nt.nodes.new(inner_node.bl_idname)
		                      new_node.location = (group_node.location.x + inner_node.location.x,
		                                           group_node.location.y + inner_node.location.y)
		                      # Copy node properties
		                      if inner_node.type == 'TEX_IMAGE':
		                          new_node.image = inner_node.image
		                      node_map[inner_node.name] = new_node
		                  # Recreate internal links
		                  for link in inner_nt.links:
		                      from_name = link.from_node.name
		                      to_name = link.to_node.name
		                      if from_name in node_map and to_name in node_map:
		                          try:
		                              nt.links.new(
		                                  node_map[from_name].outputs[link.from_socket.name],
		                                  node_map[to_name].inputs[link.to_socket.name]
		                              )
		                          except Exception:
		                              pass
		                  # Connect group outputs to whatever the group node was connected to
		                  for out_sock in group_node.outputs:
		                      if out_sock.is_linked:
		                          # Find the GROUP_OUTPUT node in the inner tree
		                          go_node = next((n for n in inner_nt.nodes if n.type == 'GROUP_OUTPUT'), None)
		                          if go_node:
		                              inner_sock = go_node.inputs.get(out_sock.name)
		                              if inner_sock and inner_sock.is_linked:
		                                  src_inner = inner_sock.links[0].from_node.name
		                                  src_sock_name = inner_sock.links[0].from_socket.name
		                                  if src_inner in node_map:
		                                      for dest_link in out_sock.links:
		                                          try:
		                                              nt.links.new(
		                                                  node_map[src_inner].outputs[src_sock_name],
		                                                  dest_link.to_socket
		                                              )
		                                          except Exception:
		                                              pass
		                  # Connect group inputs from whatever was connected to the group node
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
		                                              nt.links.new(
		                                                  in_sock.links[0].from_socket,
		                                                  node_map[dest_inner].inputs[dest_sock_name]
		                                              )
		                                          except Exception:
		                                              pass
		                  # Remove the group node
		                  nt.nodes.remove(group_node)
		      except Exception as e:
		          print('Ungroup error for ' + mat.name + ': ' + str(e))
		          break

def deduplicate_img_nodes(mat):
    """"""Remove duplicate Image Texture nodes (same image) from a material,
    keeping only one per unique image. Rewires links from removed nodes to the kept node.""""""
    if not mat.node_tree:
        return
    nt = mat.node_tree
    img_nodes = [n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image]
    seen_images = {{}}
    for node in img_nodes:
        img_name = node.image.name
        if img_name not in seen_images:
            seen_images[img_name] = node
        else:
            # Rewire all links from this duplicate to the kept node
            kept = seen_images[img_name]
            for out_idx, out_sock in enumerate(node.outputs):
                for link in list(out_sock.links):
                    try:
                        nt.links.new(kept.outputs[out_idx], link.to_socket)
                    except Exception:
                        pass
            nt.nodes.remove(node)

def find_best_bake_object(mat):
    """"""Find the mesh object with the most faces that uses this material.
    Returns (obj, slot_idx) or (None, -1) if not found.""""""
    best_obj = None
    best_slot = -1
    best_face_count = 0
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        mesh = obj.data
        if not mesh or len(mesh.polygons) == 0:
            continue
        for slot_idx, slot in enumerate(obj.material_slots):
            if slot.material == mat:
                face_count = len(mesh.polygons)
                if face_count > best_face_count:
                    best_face_count = face_count
                    best_obj = obj
                    best_slot = slot_idx
    return best_obj, best_slot

# Bake all materials FIRST (collect results), then replace them all at once.
# This avoids circular dependency issues where a previously-baked material
# on the same object interferes with the next bake.
bake_results = {{}}  # mat_name -> (mat, bake_node, bake_img)
baked_material_names = set()

for mat in bpy.data.materials:
    if mat.name in baked_material_names:
        continue
    if not mat.node_tree:
        continue
    baked_material_names.add(mat.name)
    obj, slot_idx = find_best_bake_object(mat)
    if obj is None:
        print('No valid mesh object found for material: ' + mat.name + ' - skipping bake')
        ungroup_material(mat)
        deduplicate_img_nodes(mat)
        continue
    print('Baking material: ' + mat.name + ' on object: ' + obj.name + ' (' + str(len(obj.data.polygons)) + ' faces)')
    result = bake_material_to_texture(obj, mat, slot_idx, '')
    if result:
        bake_node, bake_img = result
        bake_results[mat.name] = (mat, bake_node, bake_img)
    else:
        print('Skipping bake for ' + mat.name + ' - falling back to ungroup')
        ungroup_material(mat)
        deduplicate_img_nodes(mat)

# Now replace all materials with their baked versions
for mat_name, (mat, bake_node, bake_img) in bake_results.items():
    replace_material_with_baked(mat, bake_node, bake_img)

# Remove all unused node groups so the GLTF exporter doesn't find their Image Texture nodes
for ng in list(bpy.data.node_groups):
    if ng.users == 0:
        ng_name = ng.name  # Save name before removal invalidates the reference
        bpy.data.node_groups.remove(ng)
        print('Removed unused node group: ' + ng_name)

# Build version-safe export kwargs
ver = bpy.app.version
kwargs = dict(
	   filepath='{escapedPath}',
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
