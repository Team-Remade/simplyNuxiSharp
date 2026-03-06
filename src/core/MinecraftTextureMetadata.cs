using System;
using System.Collections.Generic;
using System.Text.Json;
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
    [JsonConverter(typeof(FramesConverter))]
    public List<FrameData> Frames { get; set; }
}

/// <summary>
/// Custom converter to handle both formats for animation frames:
/// - List of integers (frame indices)
/// - List of FrameData objects (with index and optional time)
/// </summary>
public class FramesConverter : JsonConverter<List<FrameData>>
{
    public override List<FrameData> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Frames must be an array");
        }
        
        var frames = new List<FrameData>();
        reader.Read();
        
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                // Frame is a direct integer index
                var index = reader.GetInt32();
                frames.Add(new FrameData { Index = index });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Frame is an object with index and optional time
                var frameData = JsonSerializer.Deserialize<FrameData>(ref reader, options);
                frames.Add(frameData);
            }
            else
            {
                throw new JsonException("Invalid frame format: must be number or object");
            }
            
            reader.Read();
        }
        
        return frames;
    }
    
    public override void Write(Utf8JsonWriter writer, List<FrameData> value, JsonSerializerOptions options)
    {
        // Write as array of FrameData objects
        JsonSerializer.Serialize(writer, value, options);
    }
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
