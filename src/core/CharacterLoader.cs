using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Handles loading and caching of character GLB files
/// </summary>
public class CharacterLoader
{
	private static CharacterLoader _instance;
	public static CharacterLoader Instance => _instance ??= new CharacterLoader();
	
	private readonly string _projectAssetsPath;
	private Dictionary<string, string> _characterPaths; // Name -> full path
	private bool _isLoaded = false;
	
	public bool IsLoaded => _isLoaded;
	public int TotalCharactersFound => _characterPaths?.Count ?? 0;
	
	private CharacterLoader()
	{
		// Use the user data directory path for assets
		var userDataPath = OS.GetUserDataDir();
		_projectAssetsPath = Path.Combine(userDataPath, "data", "SimplyRemadeAssetsV1");
		_characterPaths = new Dictionary<string, string>();
		
		GD.Print($"CharacterLoader initialized with assets path: {_projectAssetsPath}");
	}
	
	/// <summary>
	/// Scans for all GLB files in the SimplyRemadeAssetsV1 directory
	/// </summary>
	public async Task<bool> LoadCharacterList(Action<string, float> progressCallback = null)
	{
		if (_isLoaded)
		{
			GD.Print("Character list already loaded.");
			return true;
		}
		
		GD.Print($"Starting to scan for character GLB files from: {_projectAssetsPath}");
		
		if (!Directory.Exists(_projectAssetsPath))
		{
			GD.PrintErr($"Assets path does not exist: {_projectAssetsPath}");
			progressCallback?.Invoke($"Error: Assets directory not found", 0);
			return false;
		}
		
		try
		{
			progressCallback?.Invoke("Scanning for character files...", 10);
			
			// Search for all GLB files recursively in the SimplyRemadeAssetsV1 folder
			var glbFiles = Directory.GetFiles(_projectAssetsPath, "*.glb", SearchOption.AllDirectories);
			
			GD.Print($"Found {glbFiles.Length} GLB character files.");
			progressCallback?.Invoke($"Found {glbFiles.Length} character files", 50);
			
			_characterPaths.Clear();
			
			foreach (var filePath in glbFiles)
			{
				// Get a display name from the file name
				var fileName = Path.GetFileNameWithoutExtension(filePath);
				var displayName = CleanCharacterName(fileName);
				
				// Store the full path
				_characterPaths[displayName] = filePath;
				
				GD.Print($"  Found character: {displayName} at {filePath}");
			}
			
			_isLoaded = true;
			progressCallback?.Invoke($"Completed: {_characterPaths.Count} characters found", 100);
			
			GD.Print($"Character scanning complete. Total characters: {_characterPaths.Count}");
			
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error scanning for character files: {ex.Message}");
			GD.PrintErr($"Stack trace: {ex.StackTrace}");
			progressCallback?.Invoke($"Error: {ex.Message}", 0);
			return false;
		}
	}
	
	/// <summary>
	/// Cleans up character name for display
	/// </summary>
	private string CleanCharacterName(string fileName)
	{
		// Convert underscores to spaces and capitalize words
		var cleaned = fileName.Replace("_", " ").Replace("-", " ");
		var words = cleaned.Split(' ');
		for (int i = 0; i < words.Length; i++)
		{
			if (words[i].Length > 0)
			{
				words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
			}
		}
		return string.Join(" ", words);
	}
	
	/// <summary>
	/// Gets all character names
	/// </summary>
	public List<string> GetAllCharacterNames()
	{
		return _characterPaths.Keys.OrderBy(k => k).ToList();
	}
	
	/// <summary>
	/// Gets the full path to a character GLB file
	/// </summary>
	public string GetCharacterPath(string characterName)
	{
		if (_characterPaths.TryGetValue(characterName, out var path))
		{
			return path;
		}
		return null;
	}
	
	/// <summary>
	/// Checks if a character exists
	/// </summary>
	public bool HasCharacter(string characterName)
	{
		return _characterPaths.ContainsKey(characterName);
	}
	
	/// <summary>
	/// Clears the character list
	/// </summary>
	public void Clear()
	{
		_characterPaths.Clear();
		_isLoaded = false;
	}
}
