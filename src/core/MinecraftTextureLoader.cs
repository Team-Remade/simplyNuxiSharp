using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Handles loading and caching of Minecraft texture files
/// </summary>
public class MinecraftTextureLoader
{
	private static MinecraftTextureLoader _instance;
	public static MinecraftTextureLoader Instance => _instance ??= new MinecraftTextureLoader();
	
	private readonly string _projectAssetsPath;
	private Dictionary<string, ImageTexture> _loadedTextures;
	private bool _isLoaded = false;
	private int _totalTexturesLoaded = 0;
	private int _totalTexturesFailedToLoad = 0;
	
	public bool IsLoaded => _isLoaded;
	public int TotalTexturesLoaded => _totalTexturesLoaded;
	public int TotalTexturesFailed => _totalTexturesFailedToLoad;
	
	private MinecraftTextureLoader()
	{
		// Use the user data directory path for assets
		var userDataPath = OS.GetUserDataDir();
		_projectAssetsPath = Path.Combine(userDataPath, "data", "SimplyRemadeAssetsV1");
		_loadedTextures = new Dictionary<string, ImageTexture>();
		
		GD.Print($"MinecraftTextureLoader initialized with assets path: {_projectAssetsPath}");
	}
	
	/// <summary>
	/// Loads all texture files from the block and item folders
	/// </summary>
	/// <param name="progressCallback">Optional callback to report progress (message, percentage)</param>
	public async Task<bool> LoadAllTextures(Action<string, float> progressCallback = null)
	{
		if (_isLoaded)
		{
			GD.Print("Minecraft textures already loaded.");
			return true;
		}
		
		GD.Print($"Starting to load Minecraft textures from: {_projectAssetsPath}");
		
		var texturesPath = Path.Combine(_projectAssetsPath, "assets", "minecraft", "textures");
		
		if (!Directory.Exists(texturesPath))
		{
			GD.PrintErr($"Textures path does not exist: {texturesPath}");
			progressCallback?.Invoke($"Error: Textures directory not found", 0);
			return false;
		}
		
		try
		{
			_totalTexturesLoaded = 0;
			_totalTexturesFailedToLoad = 0;
			
			progressCallback?.Invoke("Scanning for texture files...", 5);
			
			// Get all PNG files from block and item folders
			var blockPath = Path.Combine(texturesPath, "block");
			var itemPath = Path.Combine(texturesPath, "item");
			
			var textureFiles = new List<string>();
			
			if (Directory.Exists(blockPath))
			{
				textureFiles.AddRange(Directory.GetFiles(blockPath, "*.png", SearchOption.TopDirectoryOnly));
			}
			
			if (Directory.Exists(itemPath))
			{
				textureFiles.AddRange(Directory.GetFiles(itemPath, "*.png", SearchOption.TopDirectoryOnly));
			}
			
			GD.Print($"Found {textureFiles.Count} texture files to load.");
			progressCallback?.Invoke($"Found {textureFiles.Count} texture files to load", 10);
			
			int processed = 0;
			foreach (var filePath in textureFiles)
			{
				processed++;
				
				// Report progress periodically
				if (processed % 50 == 0 || processed == textureFiles.Count)
				{
					var progress = 10 + (int)((processed / (float)textureFiles.Count) * 85);
					var message = $"Loading textures: {processed}/{textureFiles.Count}";
					progressCallback?.Invoke(message, progress);
					GD.Print($"Processing texture {processed}/{textureFiles.Count}...");
					// Allow other operations to run
					await Task.Delay(1);
				}
				
				await LoadTextureFile(filePath, texturesPath);
			}
			
			_isLoaded = true;
			GD.Print($"Finished loading Minecraft textures. Loaded: {_totalTexturesLoaded}, Failed: {_totalTexturesFailedToLoad}");
			
			progressCallback?.Invoke($"Completed: {_totalTexturesLoaded} textures loaded", 100);
			
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error loading Minecraft textures: {ex.Message}");
			GD.PrintErr($"Stack trace: {ex.StackTrace}");
			progressCallback?.Invoke($"Error: {ex.Message}", 0);
			return false;
		}
	}
	
	/// <summary>
	/// Loads a single texture file
	/// </summary>
	private async Task LoadTextureFile(string filePath, string texturesBasePath)
	{
		try
		{
			var image = new Image();
			var error = image.Load(filePath);
			
			if (error != Error.Ok)
			{
				_totalTexturesFailedToLoad++;
				GD.PrintErr($"Failed to load texture file {filePath}: {error}");
				return;
			}
			
			// Create ImageTexture from the loaded image
			var texture = ImageTexture.CreateFromImage(image);
			
			// Get relative path from textures directory for the key
			var relativePath = GetRelativePath(filePath, texturesBasePath);
			
			// Store the texture
			_loadedTextures[relativePath] = texture;
			_totalTexturesLoaded++;
			
			await Task.CompletedTask; // Satisfy async requirement
		}
		catch (Exception ex)
		{
			_totalTexturesFailedToLoad++;
			GD.PrintErr($"Failed to load texture file {filePath}: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Gets a relative path from the textures directory
	/// </summary>
	private string GetRelativePath(string fullPath, string basePath)
	{
		if (fullPath.StartsWith(basePath))
		{
			var relativePath = fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			// Normalize path separators to forward slashes for consistency
			return relativePath.Replace('\\', '/');
		}
		return fullPath;
	}
	
	/// <summary>
	/// Gets a loaded texture by its relative path
	/// </summary>
	/// <param name="relativePath">Path like "block/stone.png" or "item/diamond.png"</param>
	public ImageTexture GetTexture(string relativePath)
	{
		// Normalize the path
		var normalizedPath = relativePath.Replace('\\', '/');
		
		if (_loadedTextures.TryGetValue(normalizedPath, out var texture))
		{
			return texture;
		}
		return null;
	}
	
	/// <summary>
	/// Gets a texture by block name (searches in block folder)
	/// </summary>
	/// <param name="blockName">Block name without path, e.g., "stone"</param>
	public ImageTexture GetBlockTexture(string blockName)
	{
		var texturePath = $"block/{blockName}.png";
		return GetTexture(texturePath);
	}
	
	/// <summary>
	/// Gets a texture by item name (searches in item folder)
	/// </summary>
	/// <param name="itemName">Item name without path, e.g., "diamond"</param>
	public ImageTexture GetItemTexture(string itemName)
	{
		var texturePath = $"item/{itemName}.png";
		return GetTexture(texturePath);
	}
	
	/// <summary>
	/// Gets all loaded texture paths
	/// </summary>
	public IEnumerable<string> GetAllTexturePaths()
	{
		return _loadedTextures.Keys;
	}
	
	/// <summary>
	/// Gets all loaded block texture paths
	/// </summary>
	public IEnumerable<string> GetAllBlockTexturePaths()
	{
		return _loadedTextures.Keys.Where(path => path.StartsWith("block/"));
	}
	
	/// <summary>
	/// Gets all loaded item texture paths
	/// </summary>
	public IEnumerable<string> GetAllItemTexturePaths()
	{
		return _loadedTextures.Keys.Where(path => path.StartsWith("item/"));
	}
	
	/// <summary>
	/// Checks if a texture exists
	/// </summary>
	public bool HasTexture(string relativePath)
	{
		var normalizedPath = relativePath.Replace('\\', '/');
		return _loadedTextures.ContainsKey(normalizedPath);
	}
	
	/// <summary>
	/// Clears all loaded textures
	/// </summary>
	public void Clear()
	{
		_loadedTextures.Clear();
		_isLoaded = false;
		_totalTexturesLoaded = 0;
		_totalTexturesFailedToLoad = 0;
	}
}
