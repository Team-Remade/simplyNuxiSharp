using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace simplyRemadeNuxi.core;

// ---------------------------------------------------------------------------
// Data model for the material node-graph JSON sidecar produced by the Python
// export script.  Each .blend export writes a <name>_materials.json file next
// to the .glb that contains one BlenderMaterialFile per exported .blend file.
// ---------------------------------------------------------------------------

/// <summary>
/// Root object of the material sidecar JSON.
/// Maps material name → BlenderMaterialInfo.
/// </summary>
public class BlenderMaterialFile
{
	[JsonPropertyName("materials")]
	public Dictionary<string, BlenderMaterialInfo> Materials { get; set; } = new();
}

/// <summary>
/// All information extracted from a single Blender material.
/// </summary>
public class BlenderMaterialInfo
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	/// <summary>Whether the material uses nodes (node_tree != null).</summary>
	[JsonPropertyName("use_nodes")]
	public bool UseNodes { get; set; }

	/// <summary>All shader nodes in the material's node tree.</summary>
	[JsonPropertyName("nodes")]
	public List<BlenderNode> Nodes { get; set; } = new();

	/// <summary>All links between node sockets.</summary>
	[JsonPropertyName("links")]
	public List<BlenderLink> Links { get; set; } = new();

	/// <summary>Blend mode: OPAQUE, CLIP, BLEND, HASHED.</summary>
	[JsonPropertyName("blend_mode")]
	public string BlendMode { get; set; } = "OPAQUE";

	/// <summary>Alpha threshold when blend_mode == CLIP.</summary>
	[JsonPropertyName("alpha_threshold")]
	public float AlphaThreshold { get; set; } = 0.5f;

	/// <summary>Whether backfaces are culled.</summary>
	[JsonPropertyName("use_backface_culling")]
	public bool UseBackfaceCulling { get; set; } = true;
}

/// <summary>
/// A single node in a Blender material node tree.
/// </summary>
public class BlenderNode
{
	/// <summary>Unique name within the node tree (e.g. "Principled BSDF").</summary>
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	/// <summary>
	/// Blender node type identifier, e.g. BSDF_PRINCIPLED, TEX_IMAGE,
	/// MIX_RGB, MATH, NORMAL_MAP, etc.
	/// </summary>
	[JsonPropertyName("type")]
	public string Type { get; set; } = "";

	/// <summary>
	/// For TEX_IMAGE nodes: the image name (used to match the GLTF texture).
	/// </summary>
	[JsonPropertyName("image_name")]
	public string? ImageName { get; set; }

	/// <summary>
	/// For TEX_IMAGE nodes: colour space ("sRGB", "Non-Color", "Linear", …).
	/// </summary>
	[JsonPropertyName("color_space")]
	public string? ColorSpace { get; set; }

	/// <summary>
	/// Default (unlinked) input values keyed by socket name.
	/// Values are stored as arrays: [r,g,b,a] for colour/vector, [v] for scalar.
	/// </summary>
	[JsonPropertyName("inputs")]
	public Dictionary<string, float[]> Inputs { get; set; } = new();

	/// <summary>
	/// For MATH / MIX_RGB / etc. nodes: the operation string (e.g. "MULTIPLY").
	/// </summary>
	[JsonPropertyName("operation")]
	public string? Operation { get; set; }

	/// <summary>
	/// For NORMAL_MAP nodes: the space ("TANGENT", "OBJECT", "WORLD").
	/// </summary>
	[JsonPropertyName("space")]
	public string? Space { get; set; }

	/// <summary>
	/// For MAPPING nodes: vector type ("POINT", "TEXTURE", "VECTOR", "NORMAL").
	/// </summary>
	[JsonPropertyName("vector_type")]
	public string? VectorType { get; set; }

	/// <summary>
	/// For VALUE nodes: the constant float value.
	/// </summary>
	[JsonPropertyName("value")]
	public float? Value { get; set; }

	/// <summary>
	/// For RGB nodes: the constant colour [r,g,b,a].
	/// </summary>
	[JsonPropertyName("color")]
	public float[]? Color { get; set; }

	/// <summary>
	/// For MIX_SHADER / ADD_SHADER: no extra data needed beyond links.
	/// For FRESNEL: IOR default value is in Inputs["IOR"].
	/// </summary>
}

/// <summary>
/// A directed link between two node sockets.
/// </summary>
public class BlenderLink
{
	[JsonPropertyName("from_node")]
	public string FromNode { get; set; } = "";

	[JsonPropertyName("from_socket")]
	public string FromSocket { get; set; } = "";

	[JsonPropertyName("to_node")]
	public string ToNode { get; set; } = "";

	[JsonPropertyName("to_socket")]
	public string ToSocket { get; set; } = "";
}
