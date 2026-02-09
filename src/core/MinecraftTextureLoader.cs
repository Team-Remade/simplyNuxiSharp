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
	private readonly string _dataPath;
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
		_dataPath = Path.Combine(userDataPath, "data");
		_projectAssetsPath = Path.Combine(_dataPath, "SimplyRemadeAssetsV1");
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
		
		try
		{
			_totalTexturesLoaded = 0;
			_totalTexturesFailedToLoad = 0;
			
			progressCallback?.Invoke("Scanning for texture files...", 5);
			
			// Find all asset folders in the data directory
			var assetFolders = GetAssetFolders();
			GD.Print($"Found {assetFolders.Count} asset folder(s) to load textures from: {string.Join(", ", assetFolders.Select(Path.GetFileName))}");
			
			// Keep track of texture files with their base paths and namespace
			var textureFilesWithBasePath = new List<(string filePath, string basePath, string namespaceName)>();
			
			// Load textures from each asset folder
			foreach (var assetFolder in assetFolders)
			{
				var assetsRootPath = Path.Combine(assetFolder, "assets");
				
				if (!Directory.Exists(assetsRootPath))
				{
					GD.PrintRich($"[color=yellow]Assets path does not exist in {Path.GetFileName(assetFolder)}: {assetsRootPath}[/color]");
					continue;
				}
				
				// Look for all namespace folders (e.g., "minecraft", "farmersdelight", etc.)
				var namespaceFolders = Directory.GetDirectories(assetsRootPath);
				
				foreach (var namespaceFolder in namespaceFolders)
				{
					var namespaceName = Path.GetFileName(namespaceFolder);
					var texturesPath = Path.Combine(namespaceFolder, "textures");
					
					if (!Directory.Exists(texturesPath))
					{
						continue;
					}
					
					// Get all PNG files from block and item folders
					var blockPath = Path.Combine(texturesPath, "block");
					var itemPath = Path.Combine(texturesPath, "item");
					
					int blockCount = 0;
					int itemCount = 0;
					
					if (Directory.Exists(blockPath))
					{
						var blockFiles = Directory.GetFiles(blockPath, "*.png", SearchOption.TopDirectoryOnly);
						foreach (var file in blockFiles)
						{
							textureFilesWithBasePath.Add((file, texturesPath, namespaceName));
						}
						blockCount = blockFiles.Length;
					}
					
					if (Directory.Exists(itemPath))
					{
						var itemFiles = Directory.GetFiles(itemPath, "*.png", SearchOption.TopDirectoryOnly);
						foreach (var file in itemFiles)
						{
							textureFilesWithBasePath.Add((file, texturesPath, namespaceName));
						}
						itemCount = itemFiles.Length;
					}
					
					if (blockCount > 0 || itemCount > 0)
					{
						GD.Print($"Found {blockCount} block and {itemCount} item textures in {namespaceName} namespace ({Path.GetFileName(assetFolder)})");
					}
				}
			}
			
			GD.Print($"Found {textureFilesWithBasePath.Count} texture files total to load.");
			progressCallback?.Invoke($"Found {textureFilesWithBasePath.Count} texture files to load", 10);
			
			int processed = 0;
			foreach (var (filePath, basePath, namespaceName) in textureFilesWithBasePath)
			{
				processed++;
				
				// Report progress periodically
				if (processed % 50 == 0 || processed == textureFilesWithBasePath.Count)
				{
					var progress = 10 + (int)((processed / (float)textureFilesWithBasePath.Count) * 85);
					var message = $"Loading textures: {processed}/{textureFilesWithBasePath.Count}";
					progressCallback?.Invoke(message, progress);
					GD.Print($"Processing texture {processed}/{textureFilesWithBasePath.Count}...");
					// Allow other operations to run
					await Task.Delay(1);
				}
				
				await LoadTextureFile(filePath, basePath, namespaceName);
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
	/// Gets all asset folders in the data directory (SimplyRemadeAssetsV1 and any additional folders)
	/// </summary>
	private List<string> GetAssetFolders()
	{
		var assetFolders = new List<string>();
		
		// Always include the main assets folder first
		if (Directory.Exists(_projectAssetsPath))
		{
			assetFolders.Add(_projectAssetsPath);
		}
		
		// Look for additional asset folders in the data directory
		if (Directory.Exists(_dataPath))
		{
			var allFolders = Directory.GetDirectories(_dataPath);
			foreach (var folder in allFolders)
			{
				var folderName = Path.GetFileName(folder);
				// Include folders that end with "Assets" (e.g., "FarmersDelightAssets")
				// but not the main SimplyRemadeAssetsV1 folder (already added)
				if (folderName != "SimplyRemadeAssetsV1" && 
				    (folderName.EndsWith("Assets", StringComparison.OrdinalIgnoreCase) || 
				     folderName.Contains("Assets", StringComparison.OrdinalIgnoreCase)))
				{
					assetFolders.Add(folder);
					GD.Print($"Found additional asset folder: {folderName}");
				}
			}
		}
		
		return assetFolders;
	}
	
	/// <summary>
	/// Loads a single texture file
	/// </summary>
	private async Task LoadTextureFile(string filePath, string texturesBasePath, string namespaceName)
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
			
			// Store the texture with namespace prefix (e.g., "farmersdelight/block/stove.png")
			var textureKey = $"{namespaceName}/{relativePath}";
			_loadedTextures[textureKey] = texture;
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
	/// <param name="blockName">Block name without path, e.g., "stone" or "farmersdelight:stove"</param>
	public ImageTexture GetBlockTexture(string blockName)
	{
		// Check if namespace is specified
		if (blockName.Contains(":"))
		{
			var parts = blockName.Split(':', 2);
			var texturePath = $"{parts[0]}/block/{parts[1]}.png";
			return GetTexture(texturePath);
		}
		
		// Default to minecraft namespace
		var defaultPath = $"minecraft/block/{blockName}.png";
		return GetTexture(defaultPath);
	}
	
	/// <summary>
	/// Gets a texture by item name (searches in item folder)
	/// </summary>
	/// <param name="itemName">Item name without path, e.g., "diamond" or "farmersdelight:tomato"</param>
	public ImageTexture GetItemTexture(string itemName)
	{
		// Check if namespace is specified
		if (itemName.Contains(":"))
		{
			var parts = itemName.Split(':', 2);
			var texturePath = $"{parts[0]}/item/{parts[1]}.png";
			return GetTexture(texturePath);
		}
		
		// Default to minecraft namespace
		var defaultPath = $"minecraft/item/{itemName}.png";
		return GetTexture(defaultPath);
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
		return _loadedTextures.Keys.Where(path => path.Contains("/block/"));
	}
	
	/// <summary>
	/// Gets all loaded item texture paths
	/// </summary>
	public IEnumerable<string> GetAllItemTexturePaths()
	{
		return _loadedTextures.Keys.Where(path => path.Contains("/item/"));
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
