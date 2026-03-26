using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
	private readonly string _dataPath;
	private ConcurrentDictionary<string, MinecraftModel> _loadedModels;
	private ConcurrentDictionary<string, BlockState> _loadedBlockStates;
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
		_dataPath = Path.Combine(userDataPath, "data");
		_assetsPath = Path.Combine(_dataPath, "SimplyRemadeAssetsV1");
		_loadedModels = new ConcurrentDictionary<string, MinecraftModel>(StringComparer.OrdinalIgnoreCase);
		_loadedBlockStates = new ConcurrentDictionary<string, BlockState>(StringComparer.OrdinalIgnoreCase);
	}
	
	/// <summary>
	/// Loads all Minecraft JSON files from the assets directory using 3 parallel threads.
	/// </summary>
	/// <param name="progressCallback">Optional callback to report progress (message, percentage)</param>
	public async Task<bool> LoadAllJsonFiles(Action<string, float> progressCallback = null)
	{
		if (_isLoaded)
		{
			return true;
		}
				
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
			
			// Find all asset folders in the data directory
			var assetFolders = GetAssetFolders();
			
			// Load all JSON files recursively from all asset folders
			var jsonFiles = new List<string>();
			foreach (var assetFolder in assetFolders)
			{
				if (Directory.Exists(assetFolder))
				{
					var folderFiles = Directory.GetFiles(assetFolder, "*.json", SearchOption.AllDirectories);
					jsonFiles.AddRange(folderFiles);
				}
			}
						
			progressCallback?.Invoke($"Found {jsonFiles.Count} files to load", 10);
			
			int total = jsonFiles.Count;
			int processed = 0;

			// Use a SemaphoreSlim to cap concurrency at 3 threads
			var semaphore = new SemaphoreSlim(3, 3);

			// Kick off all file loads concurrently (max 3 at a time)
			var tasks = jsonFiles.Select(async filePath =>
			{
				await semaphore.WaitAsync();
				try
				{
					await LoadJsonFile(filePath);
				}
				finally
				{
					semaphore.Release();
					Interlocked.Increment(ref processed);
				}
			}).ToList();

			// Poll progress while tasks are running
			while (!Task.WhenAll(tasks).IsCompleted)
			{
				int snap = Volatile.Read(ref processed);
				var progress = 10 + (int)((snap / (float)total) * 85);
				progressCallback?.Invoke($"Loading JSON files: {snap}/{total}", progress);
				await Task.Delay(50);
			}

			// Await to propagate any exceptions
			await Task.WhenAll(tasks);
			
			_isLoaded = true;
			
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
	/// Loads a single JSON file and determines its type.
	/// Safe to call from multiple threads concurrently.
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
			
			Interlocked.Increment(ref _totalFilesLoaded);
		}
		catch (Exception ex)
		{
			Interlocked.Increment(ref _totalFilesFailedToLoad);
			GD.PrintErr($"Failed to load JSON file {filePath}: {ex.Message}");
		}
	}
	
	// Shared JsonSerializerOptions instance (thread-safe for reads after construction)
	private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip
	};

	/// <summary>
	/// Loads a blockstate JSON file. Thread-safe.
	/// </summary>
	private void LoadBlockStateFile(string filePath, string jsonString)
	{
		try
		{
			var blockState = JsonSerializer.Deserialize<BlockState>(jsonString, _jsonOptions);
			
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
	/// Loads a model JSON file. Thread-safe.
	/// </summary>
	private void LoadModelFile(string filePath, string jsonString)
	{
		try
		{
			var model = JsonSerializer.Deserialize<MinecraftModel>(jsonString, _jsonOptions);
			
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
	/// Tries to load a JSON file as either model or blockstate. Thread-safe.
	/// </summary>
	private void LoadGenericJsonFile(string filePath, string jsonString)
	{
		// Try to parse as model first (more common)
		try
		{
			var model = JsonSerializer.Deserialize<MinecraftModel>(jsonString, _jsonOptions);
			
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
			var blockState = JsonSerializer.Deserialize<BlockState>(jsonString, _jsonOptions);
			
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
	/// Gets all asset folders in the data directory (SimplyRemadeAssetsV1 and any additional folders)
	/// </summary>
	private List<string> GetAssetFolders()
	{
		var assetFolders = new List<string>();
		
		// Always include the main assets folder first
		if (Directory.Exists(_assetsPath))
		{
			assetFolders.Add(_assetsPath);
		}
		
		// Look for additional asset folders in the data directory
		if (Directory.Exists(_dataPath))
		{
			var allFolders = Directory.GetDirectories(_dataPath);
			foreach (var folder in allFolders)
			{
				var folderName = Path.GetFileName(folder);
				// Include folders that end with "Assets" (e.g., "FarmersDelightAssets")
				// but not any SimplyRemadeAssets folders (those come from the main asset download)
				if (!folderName.StartsWith("SimplyRemadeAssets", StringComparison.OrdinalIgnoreCase) && 
				    (folderName.EndsWith("Assets", StringComparison.OrdinalIgnoreCase) || 
				     folderName.Contains("Assets", StringComparison.OrdinalIgnoreCase)))
				{
					assetFolders.Add(folder);
				}
			}
		}
		
		return assetFolders;
	}
	
	/// <summary>
	/// Gets a relative path from the assets directory
	/// </summary>
	private string GetRelativePath(string fullPath)
	{
		// Try to get relative path from any of the asset folders
		if (fullPath.StartsWith(_assetsPath))
		{
			return fullPath.Substring(_assetsPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		
		// Check if it's from an additional asset folder in the data directory
		if (fullPath.StartsWith(_dataPath))
		{
			var relativePath = fullPath.Substring(_dataPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			// Prepend the asset folder name to maintain uniqueness
			return relativePath;
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
		Interlocked.Exchange(ref _totalFilesLoaded, 0);
		Interlocked.Exchange(ref _totalFilesFailedToLoad, 0);
	}
}
