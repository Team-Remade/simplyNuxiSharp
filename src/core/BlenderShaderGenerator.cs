using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Converts a <see cref="BlenderMaterialInfo"/> node graph into a Godot spatial
/// shader string.  The generated shader attempts to faithfully reproduce the
/// Blender node network using GLSL.
///
/// Supported node types
/// --------------------
///   BSDF_PRINCIPLED   – Principled BSDF (maps to Godot PBR outputs)
///   TEX_IMAGE         – Image Texture (sampler2D uniform + UV lookup)
///   MIX_RGB           – Mix / Blend colour nodes (all blend modes)
///   MATH              – Math node (all operations)
///   NORMAL_MAP        – Normal Map node (tangent-space)
///   BUMP              – Bump node (height → normal)
///   MAPPING           – UV Mapping node (scale / offset / rotation)
///   TEX_COORD         – Texture Coordinate node (UV output only)
///   VALUE             – Constant float
///   RGB               – Constant colour
///   INVERT            – Invert colour
///   GAMMA             – Gamma correction
///   HUE_SAT           – Hue/Saturation/Value
///   SEPRGB / COMBRGB  – Separate / Combine RGB
///   SEPXYZ / COMBXYZ  – Separate / Combine XYZ
///   FRESNEL           – Fresnel node
///   LAYER_WEIGHT      – Layer Weight node
///   MIX_SHADER        – Mix Shader (blends two BSDF results)
///   ADD_SHADER        – Add Shader
///   OUTPUT_MATERIAL   – Material Output (terminal node)
///
/// Any unrecognised node type is emitted as a comment and its outputs default
/// to zero / white so the shader still compiles.
/// </summary>
public static class BlenderShaderGenerator
{
	// -------------------------------------------------------------------------
	// Public entry point
	// -------------------------------------------------------------------------

	/// <summary>
	/// Generates a complete Godot spatial shader source string from the given
	/// Blender material node graph.
	/// </summary>
	/// <param name="mat">Parsed material info from the JSON sidecar.</param>
	/// <param name="textureNames">
	/// Ordered list of texture/image names that will be bound as sampler2D
	/// uniforms.  The index in this list determines the uniform name
	/// (tex_0, tex_1, …).
	/// </param>
	/// <returns>A complete .gdshader source string.</returns>
	public static string Generate(BlenderMaterialInfo mat, List<string> textureNames)
	{
		var ctx = new GeneratorContext(mat, textureNames);
		return ctx.Build();
	}

	// -------------------------------------------------------------------------
	// Internal generator context
	// -------------------------------------------------------------------------

	private class GeneratorContext
	{
		private readonly BlenderMaterialInfo _mat;
		private readonly List<string> _textureNames;

		// Maps image name → uniform name (tex_0, tex_1, …)
		private readonly Dictionary<string, string> _texUniformMap = new();

		// Maps node name → variable name prefix used in GLSL
		private readonly Dictionary<string, string> _nodeVarMap = new();

		// Topologically sorted node execution order
		private readonly List<BlenderNode> _sortedNodes = new();

		// Adjacency: to_node → list of (from_node, from_socket, to_socket)
		private readonly Dictionary<string, List<(string fromNode, string fromSocket, string toSocket)>> _inEdges = new();

		// Adjacency: from_node → list of (to_node, from_socket, to_socket)
		private readonly Dictionary<string, List<(string toNode, string fromSocket, string toSocket)>> _outEdges = new();

		// Collected GLSL uniform declarations
		private readonly StringBuilder _uniforms = new();

		// Collected GLSL fragment body lines
		private readonly StringBuilder _body = new();

		public GeneratorContext(BlenderMaterialInfo mat, List<string> textureNames)
		{
			_mat = mat;
			_textureNames = textureNames;

			// Build texture uniform map
			for (int i = 0; i < textureNames.Count; i++)
				_texUniformMap[textureNames[i]] = $"tex_{i}";

			// Build adjacency maps
			foreach (var link in mat.Links)
			{
				if (!_inEdges.ContainsKey(link.ToNode))
					_inEdges[link.ToNode] = new();
				_inEdges[link.ToNode].Add((link.FromNode, link.FromSocket, link.ToSocket));

				if (!_outEdges.ContainsKey(link.FromNode))
					_outEdges[link.FromNode] = new();
				_outEdges[link.FromNode].Add((link.ToNode, link.FromSocket, link.ToSocket));
			}

			// Assign variable name prefixes
			int idx = 0;
			foreach (var node in mat.Nodes)
				_nodeVarMap[node.Name] = $"n{idx++}";

			// Topological sort
			_sortedNodes.AddRange(TopologicalSort(mat.Nodes, _inEdges));
		}

		// ------------------------------------------------------------------
		// Build the full shader source
		// ------------------------------------------------------------------

		public string Build()
		{
			var sb = new StringBuilder();

			// --- Render mode ---
			sb.AppendLine("shader_type spatial;");

			var renderModes = new List<string>();
			if (_mat.BlendMode == "BLEND" || _mat.BlendMode == "HASHED")
				renderModes.Add("blend_mix");
			else
				renderModes.Add("depth_draw_opaque");

			// Always include depth_prepass_alpha so alpha-tested geometry writes to the
			// depth buffer correctly and doesn't render on top of opaque geometry.
			renderModes.Add("depth_prepass_alpha");

			if (!_mat.UseBackfaceCulling)
				renderModes.Add("cull_disabled");

			sb.AppendLine($"render_mode {string.Join(", ", renderModes)};");
			sb.AppendLine();

			// --- Texture uniforms ---
			foreach (var kv in _texUniformMap)
			{
				// Determine hint based on image name / colour space
				var node = _mat.Nodes.FirstOrDefault(n =>
					n.Type == "TEX_IMAGE" && n.ImageName == kv.Key);
				bool isNonColor = node?.ColorSpace is "Non-Color" or "Linear" or "Raw";
				string hint = isNonColor
					? "hint_default_white"
					: "source_color, hint_default_white";
				sb.AppendLine($"uniform sampler2D {kv.Value} : {hint};");
			}

			if (_texUniformMap.Count > 0)
				sb.AppendLine();

			// --- Generate per-node GLSL into _body ---
			foreach (var node in _sortedNodes)
				EmitNode(node);

			// --- Assemble fragment function ---
			sb.AppendLine("void fragment() {");
			sb.Append(_body);

			// Find the Material Output node and wire its Surface input to Godot outputs
			EmitOutputAssignments(sb);

			sb.AppendLine("}");

			return sb.ToString();
		}

		// ------------------------------------------------------------------
		// Emit GLSL for a single node
		// ------------------------------------------------------------------

		private void EmitNode(BlenderNode node)
		{
			_body.AppendLine($"\t// Node: {node.Name} ({node.Type})");

			switch (node.Type)
			{
				case "OUTPUT_MATERIAL":
					// Terminal – handled in EmitOutputAssignments
					break;

				case "BSDF_PRINCIPLED":
					EmitPrincipledBsdf(node);
					break;

				case "TEX_IMAGE":
					EmitTexImage(node);
					break;

				case "MIX_RGB":
				case "MIX":
					EmitMixRgb(node);
					break;

				case "MATH":
					EmitMath(node);
					break;

				case "NORMAL_MAP":
					EmitNormalMap(node);
					break;

				case "BUMP":
					EmitBump(node);
					break;

				case "MAPPING":
					EmitMapping(node);
					break;

				case "TEX_COORD":
					EmitTexCoord(node);
					break;

				case "VALUE":
					EmitValue(node);
					break;

				case "RGB":
					EmitRgb(node);
					break;

				case "INVERT":
					EmitInvert(node);
					break;

				case "GAMMA":
					EmitGamma(node);
					break;

				case "HUE_SAT":
					EmitHueSat(node);
					break;

				case "SEPRGB":
				case "SEPARATE_COLOR":
					EmitSepRgb(node);
					break;

				case "COMBRGB":
				case "COMBINE_COLOR":
					EmitCombRgb(node);
					break;

				case "SEPXYZ":
					EmitSepXyz(node);
					break;

				case "COMBXYZ":
					EmitCombXyz(node);
					break;

				case "FRESNEL":
					EmitFresnel(node);
					break;

				case "LAYER_WEIGHT":
					EmitLayerWeight(node);
					break;

				case "MIX_SHADER":
					EmitMixShader(node);
					break;

				case "ADD_SHADER":
					EmitAddShader(node);
					break;

				case "EMISSION":
					EmitEmission(node);
					break;

				case "BSDF_DIFFUSE":
					EmitDiffuseBsdf(node);
					break;

				case "BSDF_GLOSSY":
					EmitGlossyBsdf(node);
					break;

				case "BSDF_TRANSPARENT":
					EmitTransparentBsdf(node);
					break;

				case "CLAMP":
					EmitClamp(node);
					break;

				case "VECT_MATH":
					EmitVectorMath(node);
					break;

				case "RGBTOBW":
					EmitRgbToBw(node);
					break;

				default:
					_body.AppendLine($"\t// Unsupported node type: {node.Type} – outputs defaulted to zero");
					// Emit zero defaults for all outputs so downstream nodes compile
					var prefix = _nodeVarMap[node.Name];
					_body.AppendLine($"\tvec4 {prefix}_color = vec4(0.0);");
					_body.AppendLine($"\tfloat {prefix}_value = 0.0;");
					_body.AppendLine($"\tvec3 {prefix}_vector = vec3(0.0);");
					break;
			}
		}

		// ------------------------------------------------------------------
		// Node emitters
		// ------------------------------------------------------------------

		private void EmitPrincipledBsdf(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];

			// Base Color
			var baseColor = GetInputVec4(node, "Base Color", new[] { 0.8f, 0.8f, 0.8f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_base_color = {baseColor};");

			// Metallic
			var metallic = GetInputFloat(node, "Metallic", 0.0f);
			_body.AppendLine($"\tfloat {p}_metallic = {metallic};");

			// Roughness
			var roughness = GetInputFloat(node, "Roughness", 0.5f);
			_body.AppendLine($"\tfloat {p}_roughness = {roughness};");

			// IOR (used for specular approximation)
			var ior = GetInputFloat(node, "IOR", 1.45f);
			_body.AppendLine($"\tfloat {p}_ior = {ior};");

			// Specular (Blender 3.x) / Specular IOR Level (Blender 4.x)
			var specular = GetInputFloat(node, "Specular",
				GetDefaultFloat(node, "Specular IOR Level", 0.5f));
			_body.AppendLine($"\tfloat {p}_specular = {specular};");

			// Emission Color + Strength
			var emitColor = GetInputVec3(node, "Emission Color", new[] { 0.0f, 0.0f, 0.0f });
			// Blender 3.x uses "Emission" for the colour socket
			if (emitColor == "vec3(0.0, 0.0, 0.0)")
				emitColor = GetInputVec3(node, "Emission", new[] { 0.0f, 0.0f, 0.0f });
			var emitStrength = GetInputFloat(node, "Emission Strength", 1.0f);
			_body.AppendLine($"\tvec3 {p}_emission = {emitColor} * {emitStrength};");

			// Alpha
			var alpha = GetInputFloat(node, "Alpha", 1.0f);
			_body.AppendLine($"\tfloat {p}_alpha = {alpha};");

			// Normal (from Normal Map node) – only set when actually linked
			// to avoid passing world-space NORMAL into NORMAL_MAP which expects
			// a tangent-space value in [0,1] range.
			bool normalLinked = _inEdges.TryGetValue(node.Name, out var normalEdges) &&
			                    normalEdges.Any(e => e.toSocket == "Normal");
			if (normalLinked)
			{
				var normal = GetInputVec3(node, "Normal", null);
				_body.AppendLine($"\tvec3 {p}_normal = {normal};");
			}

			// Subsurface (simplified – just tint the albedo slightly)
			var ssWeight = GetInputFloat(node, "Subsurface Weight",
				GetDefaultFloat(node, "Subsurface", 0.0f));
			var ssColor = GetInputVec3(node, "Subsurface Color", new[] { 0.8f, 0.8f, 0.8f });
			_body.AppendLine($"\tvec3 {p}_albedo = mix({p}_base_color.rgb, {ssColor}, clamp({ssWeight}, 0.0, 1.0));");

			// Clearcoat
			var cc = GetInputFloat(node, "Coat Weight", GetDefaultFloat(node, "Clearcoat", 0.0f));
			var ccRough = GetInputFloat(node, "Coat Roughness", GetDefaultFloat(node, "Clearcoat Roughness", 0.03f));
			_body.AppendLine($"\tfloat {p}_clearcoat = {cc};");
			_body.AppendLine($"\tfloat {p}_clearcoat_roughness = {ccRough};");

			// Sheen
			var sheen = GetInputFloat(node, "Sheen Weight", GetDefaultFloat(node, "Sheen", 0.0f));
			_body.AppendLine($"\tfloat {p}_sheen = {sheen};");

			// Anisotropy
			var aniso = GetInputFloat(node, "Anisotropic", 0.0f);
			_body.AppendLine($"\tfloat {p}_anisotropy = {aniso};");

			// Transmission
			var trans = GetInputFloat(node, "Transmission Weight", GetDefaultFloat(node, "Transmission", 0.0f));
			_body.AppendLine($"\tfloat {p}_transmission = {trans};");

			// Composite BSDF struct (vec4 albedo_alpha, vec3 emission, floats)
			_body.AppendLine($"\tvec4 {p}_bsdf = vec4({p}_albedo, {p}_alpha);");
		}

		private void EmitTexImage(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];

			// Determine UV source
			string uv = GetInputVec2(node, "Vector", null) ?? "UV";

			if (node.ImageName != null && _texUniformMap.TryGetValue(node.ImageName, out var uniformName))
			{
				_body.AppendLine($"\tvec4 {p}_color = texture({uniformName}, {uv});");
				_body.AppendLine($"\tfloat {p}_alpha = {p}_color.a;");
			}
			else
			{
				// Image not found / not exported – use magenta as placeholder
				_body.AppendLine($"\t// TEX_IMAGE: image '{node.ImageName}' not found in export");
				_body.AppendLine($"\tvec4 {p}_color = vec4(1.0, 0.0, 1.0, 1.0);");
				_body.AppendLine($"\tfloat {p}_alpha = 1.0;");
			}
		}

		private void EmitMixRgb(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var fac = GetInputFloat(node, "Fac", 0.5f);
			var a = GetInputVec4(node, "Color1", new[] { 0.5f, 0.5f, 0.5f, 1.0f });
			var b = GetInputVec4(node, "Color2", new[] { 0.5f, 0.5f, 0.5f, 1.0f });

			// Also handle Blender 4.x socket names
			if (a == "vec4(0.5, 0.5, 0.5, 1.0)")
				a = GetInputVec4(node, "A", new[] { 0.5f, 0.5f, 0.5f, 1.0f });
			if (b == "vec4(0.5, 0.5, 0.5, 1.0)")
				b = GetInputVec4(node, "B", new[] { 0.5f, 0.5f, 0.5f, 1.0f });

			string op = node.Operation ?? "MIX";
			string expr = op switch
			{
				"MIX"        => $"mix({a}, {b}, {fac})",
				"ADD"        => $"clamp({a} + {b} * {fac}, 0.0, 1.0)",
				"SUBTRACT"   => $"clamp({a} - {b} * {fac}, 0.0, 1.0)",
				"MULTIPLY"   => $"mix({a}, {a} * {b}, {fac})",
				"SCREEN"     => $"mix({a}, vec4(1.0) - (vec4(1.0) - {a}) * (vec4(1.0) - {b}), {fac})",
				"OVERLAY"    => $"mix({a}, blender_overlay({a}, {b}), {fac})",
				"DARKEN"     => $"mix({a}, min({a}, {b}), {fac})",
				"LIGHTEN"    => $"mix({a}, max({a}, {b}), {fac})",
				"DODGE"      => $"mix({a}, {a} / max(vec4(1.0) - {b}, vec4(0.001)), {fac})",
				"BURN"       => $"mix({a}, vec4(1.0) - (vec4(1.0) - {a}) / max({b}, vec4(0.001)), {fac})",
				"DIFFERENCE" => $"mix({a}, abs({a} - {b}), {fac})",
				"EXCLUSION"  => $"mix({a}, {a} + {b} - 2.0 * {a} * {b}, {fac})",
				"DIVIDE"     => $"mix({a}, {a} / max({b}, vec4(0.001)), {fac})",
				"HUE"        => $"mix({a}, {b}, {fac})", // simplified
				"SATURATION" => $"mix({a}, {b}, {fac})", // simplified
				"VALUE"      => $"mix({a}, {b}, {fac})", // simplified
				"COLOR"      => $"mix({a}, {b}, {fac})", // simplified
				"SOFT_LIGHT" => $"mix({a}, (1.0 - 2.0 * {b}) * {a} * {a} + 2.0 * {b} * {a}, {fac})",
				"LINEAR_LIGHT" => $"mix({a}, {a} + 2.0 * {b} - 1.0, {fac})",
				_            => $"mix({a}, {b}, {fac})"
			};

			_body.AppendLine($"\tvec4 {p}_color = {expr};");
			_body.AppendLine($"\tfloat {p}_fac = {fac};");
		}

		private void EmitMath(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var v1 = GetInputFloat(node, "Value", 0.5f);
			var v2 = GetInputFloat(node, "Value_001", 0.5f);
			// Blender 4.x uses "Value", "Value_001", "Value_002"
			var v3 = GetInputFloat(node, "Value_002", 0.0f);

			string op = node.Operation ?? "ADD";
			string expr = op switch
			{
				"ADD"           => $"{v1} + {v2}",
				"SUBTRACT"      => $"{v1} - {v2}",
				"MULTIPLY"      => $"{v1} * {v2}",
				"DIVIDE"        => $"{v1} / max({v2}, 0.0001)",
				"MULTIPLY_ADD"  => $"{v1} * {v2} + {v3}",
				"POWER"         => $"pow(max({v1}, 0.0), {v2})",
				"LOGARITHM"     => $"log(max({v1}, 0.0001)) / log(max({v2}, 0.0001))",
				"SQRT"          => $"sqrt(max({v1}, 0.0))",
				"INVERSE_SQRT"  => $"inversesqrt(max({v1}, 0.0001))",
				"ABSOLUTE"      => $"abs({v1})",
				"EXPONENT"      => $"exp({v1})",
				"MINIMUM"       => $"min({v1}, {v2})",
				"MAXIMUM"       => $"max({v1}, {v2})",
				"LESS_THAN"     => $"({v1} < {v2} ? 1.0 : 0.0)",
				"GREATER_THAN"  => $"({v1} > {v2} ? 1.0 : 0.0)",
				"SIGN"          => $"sign({v1})",
				"COMPARE"       => $"(abs({v1} - {v2}) <= {v3} ? 1.0 : 0.0)",
				"SMOOTH_MIN"    => $"(({v1} + {v2} - sqrt(({v1} - {v2}) * ({v1} - {v2}) + {v3} * {v3})) * 0.5)",
				"SMOOTH_MAX"    => $"(({v1} + {v2} + sqrt(({v1} - {v2}) * ({v1} - {v2}) + {v3} * {v3})) * 0.5)",
				"ROUND"         => $"floor({v1} + 0.5)",
				"FLOOR"         => $"floor({v1})",
				"CEIL"          => $"ceil({v1})",
				"TRUNC"         => $"trunc({v1})",
				"FRACT"         => $"fract({v1})",
				"MODULO"        => $"mod({v1}, max({v2}, 0.0001))",
				"FLOORED_MODULO"=> $"mod({v1}, max({v2}, 0.0001))",
				"WRAP"          => $"mod({v1} - {v3}, max({v2} - {v3}, 0.0001)) + {v3}",
				"SNAP"          => $"floor({v1} / max({v2}, 0.0001)) * {v2}",
				"PINGPONG"      => $"abs(mod({v1} - {v2}, max(2.0 * {v2}, 0.0001)) - {v2})",
				"SINE"          => $"sin({v1})",
				"COSINE"        => $"cos({v1})",
				"TANGENT"       => $"tan({v1})",
				"ARCSINE"       => $"asin(clamp({v1}, -1.0, 1.0))",
				"ARCCOSINE"     => $"acos(clamp({v1}, -1.0, 1.0))",
				"ARCTANGENT"    => $"atan({v1})",
				"ARCTAN2"       => $"atan({v1}, {v2})",
				"SINH"          => $"sinh({v1})",
				"COSH"          => $"cosh({v1})",
				"TANH"          => $"tanh({v1})",
				"RADIANS"       => $"radians({v1})",
				"DEGREES"       => $"degrees({v1})",
				_               => $"{v1} + {v2}"
			};

			bool useClamp = node.Inputs.ContainsKey("use_clamp") && node.Inputs["use_clamp"][0] > 0.5f;
			if (useClamp)
				expr = $"clamp({expr}, 0.0, 1.0)";

			_body.AppendLine($"\tfloat {p}_value = {expr};");
		}

		private void EmitNormalMap(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var strength = GetInputFloat(node, "Strength", 1.0f);
			var color = GetInputVec3(node, "Color", new[] { 0.5f, 0.5f, 1.0f });
			// Convert from [0,1] to [-1,1] tangent-space normal
			_body.AppendLine($"\tvec3 {p}_normal_ts = normalize({color} * 2.0 - 1.0);");
			_body.AppendLine($"\t{p}_normal_ts = mix(vec3(0.0, 0.0, 1.0), {p}_normal_ts, {strength});");
			// Output as a vec3 that can be assigned to NORMAL_MAP
			_body.AppendLine($"\tvec3 {p}_normal = {p}_normal_ts;");
		}

		private void EmitBump(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var strength = GetInputFloat(node, "Strength", 1.0f);
			var height = GetInputFloat(node, "Height", 0.0f);
			// Approximate bump as a flat normal perturbed by height gradient
			_body.AppendLine($"\t// BUMP node approximation");
			_body.AppendLine($"\tvec3 {p}_normal = normalize(NORMAL + vec3(dFdx({height}), dFdy({height}), 0.0) * {strength} * 10.0);");
		}

		private void EmitMapping(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var vec = GetInputVec3(node, "Vector", null) ?? "vec3(UV, 0.0)";
			var loc = GetInputVec3(node, "Location", new[] { 0.0f, 0.0f, 0.0f });
			var rot = GetInputVec3(node, "Rotation", new[] { 0.0f, 0.0f, 0.0f });
			var scale = GetInputVec3(node, "Scale", new[] { 1.0f, 1.0f, 1.0f });

			_body.AppendLine($"\tvec3 {p}_vec_in = {vec};");
			// Apply scale then rotation (Z only for UV) then location
			_body.AppendLine($"\tvec3 {p}_scaled = {p}_vec_in * {scale};");
			_body.AppendLine($"\t// Mapping rotation (Z axis only for UV)");
			_body.AppendLine($"\tfloat {p}_cos_r = cos({rot}.z);");
			_body.AppendLine($"\tfloat {p}_sin_r = sin({rot}.z);");
			_body.AppendLine($"\tvec3 {p}_rotated = vec3(");
			_body.AppendLine($"\t\t{p}_scaled.x * {p}_cos_r - {p}_scaled.y * {p}_sin_r,");
			_body.AppendLine($"\t\t{p}_scaled.x * {p}_sin_r + {p}_scaled.y * {p}_cos_r,");
			_body.AppendLine($"\t\t{p}_scaled.z);");
			_body.AppendLine($"\tvec3 {p}_vector = {p}_rotated + {loc};");
		}

		private void EmitTexCoord(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			_body.AppendLine($"\tvec3 {p}_uv = vec3(UV, 0.0);");
			_body.AppendLine($"\tvec3 {p}_normal = NORMAL;");
			_body.AppendLine($"\tvec3 {p}_object = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;");
			_body.AppendLine($"\tvec3 {p}_camera = VERTEX;");
			_body.AppendLine($"\tvec3 {p}_window = vec3(FRAGCOORD.xy / VIEWPORT_SIZE, FRAGCOORD.z);");
			_body.AppendLine($"\tvec3 {p}_reflection = reflect(-VIEW, NORMAL);");
		}

		private void EmitValue(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			float val = node.Value ?? 0.0f;
			_body.AppendLine($"\tfloat {p}_value = {GlslFloat(val)};");
		}

		private void EmitRgb(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			float[] c = node.Color ?? new[] { 0.5f, 0.5f, 0.5f, 1.0f };
			_body.AppendLine($"\tvec4 {p}_color = vec4({GlslFloat(c[0])}, {GlslFloat(c[1])}, {GlslFloat(c[2])}, {GlslFloat(c.Length > 3 ? c[3] : 1.0f)});");
		}

		private void EmitInvert(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var fac = GetInputFloat(node, "Fac", 1.0f);
			var color = GetInputVec4(node, "Color", new[] { 0.0f, 0.0f, 0.0f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_color = mix({color}, vec4(1.0) - {color}, {fac});");
			_body.AppendLine($"\t{p}_color.a = {color}.a;"); // preserve alpha
		}

		private void EmitGamma(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var color = GetInputVec4(node, "Color", new[] { 1.0f, 1.0f, 1.0f, 1.0f });
			var gamma = GetInputFloat(node, "Gamma", 1.0f);
			_body.AppendLine($"\tvec4 {p}_color = vec4(pow(max({color}.rgb, vec3(0.0)), vec3({gamma})), {color}.a);");
		}

		private void EmitHueSat(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var hue = GetInputFloat(node, "Hue", 0.5f);
			var sat = GetInputFloat(node, "Saturation", 1.0f);
			var val = GetInputFloat(node, "Value", 1.0f);
			var fac = GetInputFloat(node, "Fac", 1.0f);
			var color = GetInputVec4(node, "Color", new[] { 0.8f, 0.8f, 0.8f, 1.0f });
			// Simplified: just apply saturation and value scaling
			_body.AppendLine($"\t// HUE_SAT simplified (hue rotation not implemented)");
			_body.AppendLine($"\tvec3 {p}_grey = vec3(dot({color}.rgb, vec3(0.299, 0.587, 0.114)));");
			_body.AppendLine($"\tvec3 {p}_sat_col = mix({p}_grey, {color}.rgb, {sat}) * {val};");
			_body.AppendLine($"\tvec4 {p}_color = vec4(mix({color}.rgb, {p}_sat_col, {fac}), {color}.a);");
		}

		private void EmitSepRgb(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var color = GetInputVec4(node, "Color", new[] { 0.0f, 0.0f, 0.0f, 1.0f });
			// Also handle "Image" socket name used in Blender 4.x
			if (color == "vec4(0.0, 0.0, 0.0, 1.0)")
				color = GetInputVec4(node, "Image", new[] { 0.0f, 0.0f, 0.0f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_src = {color};");
			_body.AppendLine($"\tfloat {p}_r = {p}_src.r;");
			_body.AppendLine($"\tfloat {p}_g = {p}_src.g;");
			_body.AppendLine($"\tfloat {p}_b = {p}_src.b;");
		}

		private void EmitCombRgb(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var r = GetInputFloat(node, "R", 0.0f);
			var g = GetInputFloat(node, "G", 0.0f);
			var b = GetInputFloat(node, "B", 0.0f);
			_body.AppendLine($"\tvec4 {p}_color = vec4({r}, {g}, {b}, 1.0);");
		}

		private void EmitSepXyz(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var vec = GetInputVec3(node, "Vector", new[] { 0.0f, 0.0f, 0.0f });
			_body.AppendLine($"\tvec3 {p}_src = {vec};");
			_body.AppendLine($"\tfloat {p}_x = {p}_src.x;");
			_body.AppendLine($"\tfloat {p}_y = {p}_src.y;");
			_body.AppendLine($"\tfloat {p}_z = {p}_src.z;");
		}

		private void EmitCombXyz(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var x = GetInputFloat(node, "X", 0.0f);
			var y = GetInputFloat(node, "Y", 0.0f);
			var z = GetInputFloat(node, "Z", 0.0f);
			_body.AppendLine($"\tvec3 {p}_vector = vec3({x}, {y}, {z});");
		}

		private void EmitFresnel(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var ior = GetInputFloat(node, "IOR", 1.45f);
			// Schlick approximation
			_body.AppendLine($"\tfloat {p}_f0 = pow(({ior} - 1.0) / ({ior} + 1.0), 2.0);");
			_body.AppendLine($"\tfloat {p}_value = {p}_f0 + (1.0 - {p}_f0) * pow(1.0 - max(dot(NORMAL, VIEW), 0.0), 5.0);");
		}

		private void EmitLayerWeight(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var blend = GetInputFloat(node, "Blend", 0.5f);
			_body.AppendLine($"\tfloat {p}_ndotv = max(dot(NORMAL, VIEW), 0.0);");
			_body.AppendLine($"\tfloat {p}_fresnel = pow(1.0 - {p}_ndotv, 5.0);");
			_body.AppendLine($"\tfloat {p}_facing = 1.0 - {p}_ndotv;");
		}

		private void EmitMixShader(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var fac = GetInputFloat(node, "Fac", 0.5f);
			// Shader inputs are BSDF structs (vec4)
			var s1 = GetInputVec4(node, "Shader", new[] { 0.8f, 0.8f, 0.8f, 1.0f });
			var s2 = GetInputVec4(node, "Shader_001", new[] { 0.8f, 0.8f, 0.8f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_bsdf = mix({s1}, {s2}, {fac});");
		}

		private void EmitAddShader(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var s1 = GetInputVec4(node, "Shader", new[] { 0.0f, 0.0f, 0.0f, 1.0f });
			var s2 = GetInputVec4(node, "Shader_001", new[] { 0.0f, 0.0f, 0.0f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_bsdf = clamp({s1} + {s2}, 0.0, 1.0);");
		}

		private void EmitEmission(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var color = GetInputVec3(node, "Color", new[] { 1.0f, 1.0f, 1.0f });
			var strength = GetInputFloat(node, "Strength", 1.0f);
			_body.AppendLine($"\tvec3 {p}_emission = {color} * {strength};");
			_body.AppendLine($"\tvec4 {p}_bsdf = vec4({color}, 1.0);");
		}

		private void EmitDiffuseBsdf(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var color = GetInputVec4(node, "Color", new[] { 0.8f, 0.8f, 0.8f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_bsdf = {color};");
		}

		private void EmitGlossyBsdf(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var color = GetInputVec4(node, "Color", new[] { 0.8f, 0.8f, 0.8f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_bsdf = {color};");
		}

		private void EmitTransparentBsdf(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var color = GetInputVec4(node, "Color", new[] { 1.0f, 1.0f, 1.0f, 1.0f });
			_body.AppendLine($"\tvec4 {p}_bsdf = vec4({color}.rgb, 0.0);");
		}

		private void EmitClamp(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var val = GetInputFloat(node, "Value", 1.0f);
			var min = GetInputFloat(node, "Min", 0.0f);
			var max = GetInputFloat(node, "Max", 1.0f);
			_body.AppendLine($"\tfloat {p}_result = clamp({val}, {min}, {max});");
		}

		private void EmitVectorMath(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var v1 = GetInputVec3(node, "Vector", new[] { 0.0f, 0.0f, 0.0f });
			var v2 = GetInputVec3(node, "Vector_001", new[] { 0.0f, 0.0f, 0.0f });
			var scale = GetInputFloat(node, "Scale", 1.0f);

			string op = node.Operation ?? "ADD";
			string vecExpr = op switch
			{
				"ADD"           => $"{v1} + {v2}",
				"SUBTRACT"      => $"{v1} - {v2}",
				"MULTIPLY"      => $"{v1} * {v2}",
				"DIVIDE"        => $"{v1} / max({v2}, vec3(0.0001))",
				"CROSS_PRODUCT" => $"cross({v1}, {v2})",
				"PROJECT"       => $"dot({v1}, {v2}) / max(dot({v2}, {v2}), 0.0001) * {v2}",
				"REFLECT"       => $"reflect({v1}, normalize({v2}))",
				"REFRACT"       => $"refract(normalize({v1}), normalize({v2}), {scale})",
				"NORMALIZE"     => $"normalize({v1})",
				"SCALE"         => $"{v1} * {scale}",
				"ABSOLUTE"      => $"abs({v1})",
				"MINIMUM"       => $"min({v1}, {v2})",
				"MAXIMUM"       => $"max({v1}, {v2})",
				"FLOOR"         => $"floor({v1})",
				"CEIL"          => $"ceil({v1})",
				"FRACTION"      => $"fract({v1})",
				"MODULO"        => $"mod({v1}, max({v2}, vec3(0.0001)))",
				"WRAP"          => $"mod({v1}, max({v2}, vec3(0.0001)))",
				"SNAP"          => $"floor({v1} / max({v2}, vec3(0.0001))) * {v2}",
				_               => $"{v1} + {v2}"
			};

			string floatExpr = op switch
			{
				"DOT_PRODUCT"   => $"dot({v1}, {v2})",
				"LENGTH"        => $"length({v1})",
				"DISTANCE"      => $"distance({v1}, {v2})",
				_               => "0.0"
			};

			_body.AppendLine($"\tvec3 {p}_vector = {vecExpr};");
			_body.AppendLine($"\tfloat {p}_value = {floatExpr};");
		}

		private void EmitRgbToBw(BlenderNode node)
		{
			var p = _nodeVarMap[node.Name];
			var color = GetInputVec4(node, "Color", new[] { 0.5f, 0.5f, 0.5f, 1.0f });
			_body.AppendLine($"\tfloat {p}_val = dot({color}.rgb, vec3(0.2126, 0.7152, 0.0722));");
		}

		// ------------------------------------------------------------------
		// Wire Material Output node to Godot fragment outputs
		// ------------------------------------------------------------------

		private void EmitOutputAssignments(StringBuilder sb)
		{
			// Find the Material Output node
			var outputNode = _mat.Nodes.FirstOrDefault(n => n.Type == "OUTPUT_MATERIAL");
			if (outputNode == null)
			{
				// Fallback: grey
				sb.AppendLine("\tALBEDO = vec3(0.8);");
				sb.AppendLine("\tMETALLIC = 0.0;");
				sb.AppendLine("\tROUGHNESS = 0.5;");
				return;
			}

			// Find what's connected to the "Surface" input of the output node
			var surfaceLink = _mat.Links.FirstOrDefault(l =>
				l.ToNode == outputNode.Name && l.ToSocket == "Surface");

			if (surfaceLink == null)
			{
				sb.AppendLine("\tALBEDO = vec3(0.8);");
				sb.AppendLine("\tMETALLIC = 0.0;");
				sb.AppendLine("\tROUGHNESS = 0.5;");
				return;
			}

			var bsdfNode = _mat.Nodes.FirstOrDefault(n => n.Name == surfaceLink.FromNode);
			if (bsdfNode == null)
			{
				sb.AppendLine("\tALBEDO = vec3(0.8);");
				return;
			}

			var bp = _nodeVarMap[bsdfNode.Name];

			switch (bsdfNode.Type)
			{
				case "BSDF_PRINCIPLED":
						sb.AppendLine($"\tALBEDO = {bp}_albedo;");
						sb.AppendLine($"\tMETALLIC = clamp({bp}_metallic, 0.0, 1.0);");
						sb.AppendLine($"\tROUGHNESS = clamp({bp}_roughness, 0.0, 1.0);");
						sb.AppendLine($"\tSPECULAR = clamp({bp}_specular, 0.0, 1.0);");
						sb.AppendLine($"\tEMISSION = {bp}_emission;");
	
						// Normal map – only assign when the Normal socket was linked
						// (avoids passing world-space NORMAL into NORMAL_MAP)
						{
							var bsdfNodeRef = _mat.Nodes.FirstOrDefault(n => n.Name == bsdfNode.Name);
							bool normalIsLinked = bsdfNodeRef != null &&
								_inEdges.TryGetValue(bsdfNodeRef.Name, out var bsdfNormalEdges) &&
								bsdfNormalEdges.Any(e => e.toSocket == "Normal");
							if (normalIsLinked)
							{
								sb.AppendLine($"\tNORMAL_MAP = {bp}_normal;");
								sb.AppendLine($"\tNORMAL_MAP_DEPTH = 1.0;");
							}
						}
	
						// Alpha
					if (_mat.BlendMode == "CLIP")
					{
						sb.AppendLine($"\tif ({bp}_alpha < {GlslFloat(_mat.AlphaThreshold)}) discard;");
						sb.AppendLine($"\tALPHA = {bp}_alpha;");
					}
					else if (_mat.BlendMode is "BLEND" or "HASHED")
					{
						sb.AppendLine($"\tALPHA = {bp}_alpha;");
					}

					// Clearcoat
					sb.AppendLine($"\tCLEARCOAT = clamp({bp}_clearcoat, 0.0, 1.0);");
					sb.AppendLine($"\tCLEARCOAT_ROUGHNESS = clamp({bp}_clearcoat_roughness, 0.0, 1.0);");
					break;

				case "EMISSION":
					sb.AppendLine($"\tALBEDO = {bp}_bsdf.rgb;");
					sb.AppendLine($"\tEMISSION = {bp}_emission;");
					sb.AppendLine($"\tMETALLIC = 0.0;");
					sb.AppendLine($"\tROUGHNESS = 1.0;");
					break;

				case "BSDF_DIFFUSE":
				case "BSDF_GLOSSY":
					sb.AppendLine($"\tALBEDO = {bp}_bsdf.rgb;");
					sb.AppendLine($"\tMETALLIC = 0.0;");
					sb.AppendLine($"\tROUGHNESS = 0.5;");
					break;

				case "MIX_SHADER":
				case "ADD_SHADER":
					sb.AppendLine($"\tALBEDO = {bp}_bsdf.rgb;");
					sb.AppendLine($"\tMETALLIC = 0.0;");
					sb.AppendLine($"\tROUGHNESS = 0.5;");
					if (_mat.BlendMode is "BLEND" or "HASHED")
						sb.AppendLine($"\tALPHA = {bp}_bsdf.a;");
					break;

				default:
					// Generic: try to use _color or _bsdf
					sb.AppendLine($"\tALBEDO = vec3(0.8);");
					sb.AppendLine($"\tMETALLIC = 0.0;");
					sb.AppendLine($"\tROUGHNESS = 0.5;");
					break;
			}
		}

		// ------------------------------------------------------------------
		// Input resolution helpers
		// ------------------------------------------------------------------

		/// <summary>
		/// Returns the raw float default value for a socket from the node's inputs dict,
		/// without following any links.  Used when a default is needed as a float (not GLSL string).
		/// </summary>
		private static float GetDefaultFloat(BlenderNode node, string socketName, float fallback)
		{
			if (node.Inputs.TryGetValue(socketName, out var vals) && vals.Length > 0)
				return vals[0];
			return fallback;
		}

		/// <summary>
		/// Returns a GLSL expression for a float input socket.
		/// If the socket is linked, returns the upstream variable reference.
		/// Otherwise returns the default value literal.
		/// </summary>
		private string GetInputFloat(BlenderNode node, string socketName, float defaultVal)
		{
			// Check if there's a link feeding this socket
			if (_inEdges.TryGetValue(node.Name, out var edges))
			{
				var link = edges.FirstOrDefault(e => e.toSocket == socketName);
				if (link != default)
				{
					var fromNode = _mat.Nodes.FirstOrDefault(n => n.Name == link.fromNode);
					if (fromNode != null)
					{
						var fp = _nodeVarMap[fromNode.Name];
						return ResolveFloatOutput(fromNode, link.fromSocket, fp);
					}
				}
			}

			// Use default value from node inputs dict
			if (node.Inputs.TryGetValue(socketName, out var vals) && vals.Length > 0)
				return GlslFloat(vals[0]);

			return GlslFloat(defaultVal);
		}

		/// <summary>
		/// Returns a GLSL expression for a vec4 input socket.
		/// </summary>
		private string GetInputVec4(BlenderNode node, string socketName, float[]? defaultVal)
		{
			if (_inEdges.TryGetValue(node.Name, out var edges))
			{
				var link = edges.FirstOrDefault(e => e.toSocket == socketName);
				if (link != default)
				{
					var fromNode = _mat.Nodes.FirstOrDefault(n => n.Name == link.fromNode);
					if (fromNode != null)
					{
						var fp = _nodeVarMap[fromNode.Name];
						return ResolveVec4Output(fromNode, link.fromSocket, fp);
					}
				}
			}

			if (node.Inputs.TryGetValue(socketName, out var vals))
			{
				if (vals.Length >= 4)
					return $"vec4({GlslFloat(vals[0])}, {GlslFloat(vals[1])}, {GlslFloat(vals[2])}, {GlslFloat(vals[3])})";
				if (vals.Length == 3)
					return $"vec4({GlslFloat(vals[0])}, {GlslFloat(vals[1])}, {GlslFloat(vals[2])}, 1.0)";
				if (vals.Length == 1)
					return $"vec4({GlslFloat(vals[0])})";
			}

			if (defaultVal != null)
			{
				if (defaultVal.Length >= 4)
					return $"vec4({GlslFloat(defaultVal[0])}, {GlslFloat(defaultVal[1])}, {GlslFloat(defaultVal[2])}, {GlslFloat(defaultVal[3])})";
				if (defaultVal.Length == 3)
					return $"vec4({GlslFloat(defaultVal[0])}, {GlslFloat(defaultVal[1])}, {GlslFloat(defaultVal[2])}, 1.0)";
			}

			return "vec4(0.0)";
		}

		/// <summary>
		/// Returns a GLSL expression for a vec3 input socket.
		/// Returns null if no value is available and defaultVal is null.
		/// </summary>
		private string? GetInputVec3(BlenderNode node, string socketName, float[]? defaultVal)
		{
			if (_inEdges.TryGetValue(node.Name, out var edges))
			{
				var link = edges.FirstOrDefault(e => e.toSocket == socketName);
				if (link != default)
				{
					var fromNode = _mat.Nodes.FirstOrDefault(n => n.Name == link.fromNode);
					if (fromNode != null)
					{
						var fp = _nodeVarMap[fromNode.Name];
						return ResolveVec3Output(fromNode, link.fromSocket, fp);
					}
				}
			}

			if (node.Inputs.TryGetValue(socketName, out var vals))
			{
				if (vals.Length >= 3)
					return $"vec3({GlslFloat(vals[0])}, {GlslFloat(vals[1])}, {GlslFloat(vals[2])})";
				if (vals.Length == 1)
					return $"vec3({GlslFloat(vals[0])})";
			}

			if (defaultVal != null)
			{
				if (defaultVal.Length >= 3)
					return $"vec3({GlslFloat(defaultVal[0])}, {GlslFloat(defaultVal[1])}, {GlslFloat(defaultVal[2])})";
				if (defaultVal.Length == 1)
					return $"vec3({GlslFloat(defaultVal[0])})";
			}

			return null;
		}

		/// <summary>
		/// Returns a GLSL expression for a vec2 input socket.
		/// Returns null if no value is available and defaultVal is null.
		/// </summary>
		private string? GetInputVec2(BlenderNode node, string socketName, float[]? defaultVal)
		{
			if (_inEdges.TryGetValue(node.Name, out var edges))
			{
				var link = edges.FirstOrDefault(e => e.toSocket == socketName);
				if (link != default)
				{
					var fromNode = _mat.Nodes.FirstOrDefault(n => n.Name == link.fromNode);
					if (fromNode != null)
					{
						var fp = _nodeVarMap[fromNode.Name];
						// Resolve the correct vec3 variable name for this node/socket,
						// then take .xy to get a vec2 for texture sampling.
						var vec3Expr = ResolveVec3Output(fromNode, link.fromSocket, fp);
						return $"{vec3Expr}.xy";
					}
				}
			}

			if (node.Inputs.TryGetValue(socketName, out var vals))
			{
				if (vals.Length >= 2)
					return $"vec2({GlslFloat(vals[0])}, {GlslFloat(vals[1])})";
			}

			return defaultVal != null && defaultVal.Length >= 2
				? $"vec2({GlslFloat(defaultVal[0])}, {GlslFloat(defaultVal[1])})"
				: null;
		}

		// ------------------------------------------------------------------
		// Output socket resolution
		// ------------------------------------------------------------------

		private string ResolveFloatOutput(BlenderNode node, string socketName, string prefix)
		{
			return node.Type switch
			{
				"MATH"          => $"{prefix}_value",
				"VALUE"         => $"{prefix}_value",
				"TEX_IMAGE"     => socketName == "Alpha" ? $"{prefix}_alpha" : $"{prefix}_color.r",
				"SEPRGB" or "SEPARATE_COLOR" => socketName switch
				{
					"R" => $"{prefix}_r",
					"G" => $"{prefix}_g",
					"B" => $"{prefix}_b",
					_   => $"{prefix}_r"
				},
				"SEPXYZ"        => socketName switch
				{
					"X" => $"{prefix}_x",
					"Y" => $"{prefix}_y",
					"Z" => $"{prefix}_z",
					_   => $"{prefix}_x"
				},
				"FRESNEL"       => $"{prefix}_value",
				"LAYER_WEIGHT"  => socketName == "Facing" ? $"{prefix}_facing" : $"{prefix}_fresnel",
				"RGBTOBW"       => $"{prefix}_val",
				"CLAMP"         => $"{prefix}_result",
				"VECT_MATH"     => $"{prefix}_value",
				"MIX_RGB" or "MIX" => $"{prefix}_fac",
				_               => $"{prefix}_value"
			};
		}

		private string ResolveVec4Output(BlenderNode node, string socketName, string prefix)
		{
			return node.Type switch
			{
				"TEX_IMAGE"     => $"{prefix}_color",
				"MIX_RGB" or "MIX" => $"{prefix}_color",
				"RGB"           => $"{prefix}_color",
				"INVERT"        => $"{prefix}_color",
				"GAMMA"         => $"{prefix}_color",
				"HUE_SAT"       => $"{prefix}_color",
				"COMBRGB" or "COMBINE_COLOR" => $"{prefix}_color",
				"BSDF_PRINCIPLED" => $"{prefix}_bsdf",
				"MIX_SHADER"    => $"{prefix}_bsdf",
				"ADD_SHADER"    => $"{prefix}_bsdf",
				"EMISSION"      => $"{prefix}_bsdf",
				"BSDF_DIFFUSE"  => $"{prefix}_bsdf",
				"BSDF_GLOSSY"   => $"{prefix}_bsdf",
				"BSDF_TRANSPARENT" => $"{prefix}_bsdf",
				"VALUE"         => $"vec4({prefix}_value)",
				"MATH"          => $"vec4({prefix}_value)",
				_               => $"{prefix}_color"
			};
		}

		private string ResolveVec3Output(BlenderNode node, string socketName, string prefix)
		{
			return node.Type switch
			{
				"TEX_IMAGE"     => $"{prefix}_color.rgb",
				"MIX_RGB" or "MIX" => $"{prefix}_color.rgb",
				"RGB"           => $"{prefix}_color.rgb",
				"INVERT"        => $"{prefix}_color.rgb",
				"GAMMA"         => $"{prefix}_color.rgb",
				"HUE_SAT"       => $"{prefix}_color.rgb",
				"COMBRGB" or "COMBINE_COLOR" => $"{prefix}_color.rgb",
				"COMBXYZ"       => $"{prefix}_vector",
				"MAPPING"       => $"{prefix}_vector",
				"TEX_COORD"     => socketName switch
				{
					"UV"         => $"{prefix}_uv",
					"Normal"     => $"{prefix}_normal",
					"Object"     => $"{prefix}_object",
					"Camera"     => $"{prefix}_camera",
					"Window"     => $"{prefix}_window",
					"Reflection" => $"{prefix}_reflection",
					_            => $"{prefix}_uv"
				},
				"NORMAL_MAP"    => $"{prefix}_normal",
				"BUMP"          => $"{prefix}_normal",
				"VECT_MATH"     => $"{prefix}_vector",
				"BSDF_PRINCIPLED" => $"{prefix}_albedo",
				"VALUE"         => $"vec3({prefix}_value)",
				"MATH"          => $"vec3({prefix}_value)",
				_               => $"{prefix}_vector"
			};
		}

		// ------------------------------------------------------------------
		// Topological sort (Kahn's algorithm)
		// ------------------------------------------------------------------

		private static List<BlenderNode> TopologicalSort(
			List<BlenderNode> nodes,
			Dictionary<string, List<(string fromNode, string fromSocket, string toSocket)>> inEdges)
		{
			var inDegree = new Dictionary<string, int>();
			foreach (var n in nodes)
				inDegree[n.Name] = 0;

			foreach (var kv in inEdges)
				if (inDegree.ContainsKey(kv.Key))
					inDegree[kv.Key] = kv.Value.Count;

			var queue = new Queue<BlenderNode>(
				nodes.Where(n => inDegree[n.Name] == 0));

			// Build reverse adjacency for Kahn's
			var outAdj = new Dictionary<string, List<string>>();
			foreach (var kv in inEdges)
				foreach (var (fromNode, _, _) in kv.Value)
				{
					if (!outAdj.ContainsKey(fromNode))
						outAdj[fromNode] = new();
					outAdj[fromNode].Add(kv.Key);
				}

			var sorted = new List<BlenderNode>();
			var nodeByName = nodes.ToDictionary(n => n.Name);

			while (queue.Count > 0)
			{
				var n = queue.Dequeue();
				sorted.Add(n);

				if (outAdj.TryGetValue(n.Name, out var successors))
				{
					foreach (var s in successors)
					{
						inDegree[s]--;
						if (inDegree[s] == 0 && nodeByName.TryGetValue(s, out var sNode))
							queue.Enqueue(sNode);
					}
				}
			}

			// Append any remaining nodes (cycles / disconnected)
			foreach (var n in nodes)
				if (!sorted.Contains(n))
					sorted.Add(n);

			return sorted;
		}

		// ------------------------------------------------------------------
		// GLSL formatting helpers
		// ------------------------------------------------------------------

		private static string GlslFloat(float v)
		{
			// Always include a decimal point so GLSL treats it as float
			var s = v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
			return s.Contains('.') || s.Contains('E') ? s : s + ".0";
		}
	}

	// -------------------------------------------------------------------------
	// Helper GLSL functions injected into the shader when needed
	// -------------------------------------------------------------------------

	// (Currently the overlay blend mode helper is inlined via a function call
	//  in the shader body.  If we need it we can add a vertex/fragment helper
	//  section.  For now the generator avoids it by using a simplified formula.)
}
