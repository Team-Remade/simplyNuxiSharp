using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Handles loading and caching of Minecraft JSON files
/// </summary>
public class MinecraftJsonLoader
{
	private static MinecraftJsonLoader _instance;
	public static MinecraftJsonLoader Instance => _instance ??= new MinecraftJsonLoader();
	
	private readonly string _assetsPath;
	private Dictionary<string, MinecraftModel> _loadedModels;
	private Dictionary<string, BlockState> _loadedBlockStates;
	private bool _isLoaded = false;
	private int _totalFilesLoaded = 0;
	private int _totalFilesFailedToLoad = 0;
	
	public bool IsLoaded => _isLoaded;
	public int TotalFilesLoaded => _totalFilesLoaded;
	public int TotalFilesFailed => _totalFilesFailedToLoad;
	
	private MinecraftJsonLoader()
	{
		// Use the user data directory path for assets
		var userDataPath = OS.GetUserDataDir();
		_assetsPath = Path.Combine(userDataPath, "data", "SimplyRemadeAssetsV1");
		_loadedModels = new Dictionary<string, MinecraftModel>();
		_loadedBlockStates = new Dictionary<string, BlockState>();
		
		GD.Print($"MinecraftJsonLoader initialized with assets path: {_assetsPath}");
	}
	
	/// <summary>
	/// Loads all Minecraft JSON files from the assets directory
	/// </summary>
	/// <param name="progressCallback">Optional callback to report progress (message, percentage)</param>
	public async Task<bool> LoadAllJsonFiles(Action<string, float> progressCallback = null)
	{
		if (_isLoaded)
		{
			GD.Print("Minecraft JSON files already loaded.");
			return true;
		}
		
		GD.Print($"Starting to load Minecraft JSON files from: {_assetsPath}");
		
		if (!Directory.Exists(_assetsPath))
		{
			GD.PrintErr($"Assets path does not exist: {_assetsPath}");
			progressCallback?.Invoke($"Error: Assets directory not found", 0);
			return false;
		}
		
		try
		{
			_totalFilesLoaded = 0;
			_totalFilesFailedToLoad = 0;
			
			progressCallback?.Invoke("Scanning for JSON files...", 5);
			
			// Load all JSON files recursively
			var jsonFiles = Directory.GetFiles(_assetsPath, "*.json", SearchOption.AllDirectories).ToList();
			GD.Print($"Found {jsonFiles.Count} JSON files to load.");
			
			progressCallback?.Invoke($"Found {jsonFiles.Count} files to load", 10);
			
			int processed = 0;
			foreach (var filePath in jsonFiles)
			{
				processed++;
				
				// Report progress periodically
				if (processed % 50 == 0 || processed == jsonFiles.Count)
				{
					var progress = 10 + (int)((processed / (float)jsonFiles.Count) * 85);
					var message = $"Loading JSON files: {processed}/{jsonFiles.Count}";
					progressCallback?.Invoke(message, progress);
					GD.Print($"Processing file {processed}/{jsonFiles.Count}...");
					// Allow other operations to run
					await Task.Delay(1);
				}
				
				await LoadJsonFile(filePath);
			}
			
			_isLoaded = true;
			GD.Print($"Finished loading Minecraft JSON files. Loaded: {_totalFilesLoaded}, Failed: {_totalFilesFailedToLoad}");
			
			// Print summary
			GD.Print($"Total models loaded: {_loadedModels.Count}");
			GD.Print($"Total blockstates loaded: {_loadedBlockStates.Count}");
			
			progressCallback?.Invoke($"Completed: {_totalFilesLoaded} files loaded", 100);
			
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error loading Minecraft JSON files: {ex.Message}");
			GD.PrintErr($"Stack trace: {ex.StackTrace}");
			progressCallback?.Invoke($"Error: {ex.Message}", 0);
			return false;
		}
	}
	
	/// <summary>
	/// Loads a single JSON file and determines its type
	/// </summary>
	private async Task LoadJsonFile(string filePath)
	{
		try
		{
			var jsonString = await File.ReadAllTextAsync(filePath);
			
			// Determine the type of JSON file based on content or path
			if (filePath.Contains("blockstates", StringComparison.OrdinalIgnoreCase))
			{
				LoadBlockStateFile(filePath, jsonString);
			}
			else if (filePath.Contains("models", StringComparison.OrdinalIgnoreCase))
			{
				LoadModelFile(filePath, jsonString);
			}
			else
			{
				// Try to auto-detect the type
				LoadGenericJsonFile(filePath, jsonString);
			}
			
			_totalFilesLoaded++;
		}
		catch (Exception ex)
		{
			_totalFilesFailedToLoad++;
			GD.PrintErr($"Failed to load JSON file {filePath}: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Loads a blockstate JSON file
	/// </summary>
	private void LoadBlockStateFile(string filePath, string jsonString)
	{
		try
		{
			var blockState = JsonSerializer.Deserialize<BlockState>(jsonString, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip
			});
			
			if (blockState != null)
			{
				var relativePath = GetRelativePath(filePath);
				_loadedBlockStates[relativePath] = blockState;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing blockstate file {filePath}: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Loads a model JSON file
	/// </summary>
	private void LoadModelFile(string filePath, string jsonString)
	{
		try
		{
			var model = JsonSerializer.Deserialize<MinecraftModel>(jsonString, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip
			});
			
			if (model != null)
			{
				var relativePath = GetRelativePath(filePath);
				_loadedModels[relativePath] = model;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing model file {filePath}: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Tries to load a JSON file as either model or blockstate
	/// </summary>
	private void LoadGenericJsonFile(string filePath, string jsonString)
	{
		// Try to parse as model first (more common)
		try
		{
			var model = JsonSerializer.Deserialize<MinecraftModel>(jsonString, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip
			});
			
			if (model != null && (model.Elements != null || model.Parent != null || model.Textures != null))
			{
				var relativePath = GetRelativePath(filePath);
				_loadedModels[relativePath] = model;
				return;
			}
		}
		catch { }
		
		// Try as blockstate
		try
		{
			var blockState = JsonSerializer.Deserialize<BlockState>(jsonString, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip
			});
			
			if (blockState != null && (blockState.Variants != null || blockState.Multipart != null))
			{
				var relativePath = GetRelativePath(filePath);
				_loadedBlockStates[relativePath] = blockState;
				return;
			}
		}
		catch { }
	}
	
	/// <summary>
	/// Gets a relative path from the assets directory
	/// </summary>
	private string GetRelativePath(string fullPath)
	{
		if (fullPath.StartsWith(_assetsPath))
		{
			return fullPath.Substring(_assetsPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		return fullPath;
	}
	
	/// <summary>
	/// Gets a loaded model by its relative path
	/// </summary>
	public MinecraftModel GetModel(string relativePath)
	{
		if (_loadedModels.TryGetValue(relativePath, out var model))
		{
			return model;
		}
		return null;
	}
	
	/// <summary>
	/// Gets a loaded blockstate by its relative path
	/// </summary>
	public BlockState GetBlockState(string relativePath)
	{
		if (_loadedBlockStates.TryGetValue(relativePath, out var blockState))
		{
			return blockState;
		}
		return null;
	}
	
	/// <summary>
	/// Gets all loaded model paths
	/// </summary>
	public IEnumerable<string> GetAllModelPaths()
	{
		return _loadedModels.Keys;
	}
	
	/// <summary>
	/// Gets all loaded blockstate paths
	/// </summary>
	public IEnumerable<string> GetAllBlockStatePaths()
	{
		return _loadedBlockStates.Keys;
	}
	
	/// <summary>
	/// Checks if a model exists
	/// </summary>
	public bool HasModel(string relativePath)
	{
		return _loadedModels.ContainsKey(relativePath);
	}
	
	/// <summary>
	/// Checks if a blockstate exists
	/// </summary>
	public bool HasBlockState(string relativePath)
	{
		return _loadedBlockStates.ContainsKey(relativePath);
	}
	
	/// <summary>
	/// Clears all loaded data
	/// </summary>
	public void Clear()
	{
		_loadedModels.Clear();
		_loadedBlockStates.Clear();
		_isLoaded = false;
		_totalFilesLoaded = 0;
		_totalFilesFailedToLoad = 0;
	}
}
