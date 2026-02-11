using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Represents the contents of a .mcmeta file for texture animation and metadata
/// </summary>
public class MinecraftTextureMetadata
{
    [JsonPropertyName("animation")]
    public AnimationData Animation { get; set; }
    
    [JsonPropertyName("texture")]
    public TextureData Texture { get; set; }
}

/// <summary>
/// Animation data for animated textures
/// </summary>
public class AnimationData
{
    /// <summary>
    /// Whether to interpolate between frames
    /// </summary>
    [JsonPropertyName("interpolate")]
    public bool Interpolate { get; set; } = false;
    
    /// <summary>
    /// Width of a frame (defaults to texture height if not specified)
    /// </summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }
    
    /// <summary>
    /// Height of a frame (defaults to texture width if not specified)
    /// </summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }
    
    /// <summary>
    /// Default time for each frame in ticks (1/20th of a second)
    /// </summary>
    [JsonPropertyName("frametime")]
    public int Frametime { get; set; } = 1;
    
    /// <summary>
    /// Custom frame sequence and timing (optional)
    /// If not specified, frames play in order from top to bottom
    /// </summary>
    [JsonPropertyName("frames")]
    public List<FrameData> Frames { get; set; }
}

/// <summary>
/// Defines a specific frame in an animation
/// </summary>
public class FrameData
{
    /// <summary>
    /// Frame index (starting from 0)
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }
    
    /// <summary>
    /// Time to display this frame in ticks (overrides default frametime)
    /// </summary>
    [JsonPropertyName("time")]
    public int? Time { get; set; }
}

/// <summary>
/// Additional texture metadata
/// </summary>
public class TextureData
{
    /// <summary>
    /// Whether to blur the texture
    /// </summary>
    [JsonPropertyName("blur")]
    public bool Blur { get; set; } = false;
    
    /// <summary>
    /// Whether to clamp the texture
    /// </summary>
    [JsonPropertyName("clamp")]
    public bool Clamp { get; set; } = false;
    
    /// <summary>
    /// Mipmapping settings
    /// </summary>
    [JsonPropertyName("mipmaps")]
    public List<string> Mipmaps { get; set; }
}
