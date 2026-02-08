using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Represents a Minecraft JSON model file
/// </summary>
public class MinecraftModel
{
	[JsonPropertyName("parent")]
	public string Parent { get; set; }
	
	[JsonPropertyName("textures")]
	public Dictionary<string, string> Textures { get; set; }
	
	[JsonPropertyName("elements")]
	public List<ModelElement> Elements { get; set; }
	
	[JsonPropertyName("display")]
	public Dictionary<string, DisplayTransform> Display { get; set; }
	
	[JsonPropertyName("ambientocclusion")]
	public bool AmbientOcclusion { get; set; } = true;
	
	[JsonPropertyName("gui_light")]
	public string GuiLight { get; set; }
}

/// <summary>
/// Represents an element (cube) in a Minecraft model
/// </summary>
public class ModelElement
{
	[JsonPropertyName("from")]
	public float[] From { get; set; }
	
	[JsonPropertyName("to")]
	public float[] To { get; set; }
	
	[JsonPropertyName("rotation")]
	public ElementRotation Rotation { get; set; }
	
	[JsonPropertyName("shade")]
	public bool Shade { get; set; } = true;
	
	[JsonPropertyName("faces")]
	public Dictionary<string, ElementFace> Faces { get; set; }
}

/// <summary>
/// Represents a rotation of a model element
/// </summary>
public class ElementRotation
{
	[JsonPropertyName("origin")]
	public float[] Origin { get; set; }
	
	[JsonPropertyName("axis")]
	public string Axis { get; set; }
	
	[JsonPropertyName("angle")]
	public float Angle { get; set; }
	
	[JsonPropertyName("rescale")]
	public bool Rescale { get; set; } = false;
}

/// <summary>
/// Represents a face of a model element
/// </summary>
public class ElementFace
{
	[JsonPropertyName("uv")]
	public float[] UV { get; set; }
	
	[JsonPropertyName("texture")]
	public string Texture { get; set; }
	
	[JsonPropertyName("cullface")]
	public string CullFace { get; set; }
	
	[JsonPropertyName("rotation")]
	public int Rotation { get; set; } = 0;
	
	[JsonPropertyName("tintindex")]
	public int TintIndex { get; set; } = -1;
}

/// <summary>
/// Represents display transform settings for different perspectives
/// </summary>
public class DisplayTransform
{
	[JsonPropertyName("rotation")]
	public float[] Rotation { get; set; }
	
	[JsonPropertyName("translation")]
	public float[] Translation { get; set; }
	
	[JsonPropertyName("scale")]
	public float[] Scale { get; set; }
}

/// <summary>
/// Represents a Minecraft blockstate JSON file
/// </summary>
public class BlockState
{
	[JsonPropertyName("variants")]
	public Dictionary<string, object> Variants { get; set; }
	
	[JsonPropertyName("multipart")]
	public List<MultipartCase> Multipart { get; set; }
}

/// <summary>
/// Represents a multipart case in a blockstate
/// </summary>
public class MultipartCase
{
	[JsonPropertyName("when")]
	public Dictionary<string, object> When { get; set; }
	
	[JsonPropertyName("apply")]
	public object Apply { get; set; }
}

/// <summary>
/// Represents a variant model reference
/// </summary>
public class VariantModel
{
	[JsonPropertyName("model")]
	public string Model { get; set; }
	
	[JsonPropertyName("x")]
	public int X { get; set; } = 0;
	
	[JsonPropertyName("y")]
	public int Y { get; set; } = 0;
	
	[JsonPropertyName("uvlock")]
	public bool UVLock { get; set; } = false;
	
	[JsonPropertyName("weight")]
	public int Weight { get; set; } = 1;
}
