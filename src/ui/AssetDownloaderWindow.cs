using Godot;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.ui;

public partial class AssetDownloaderWindow : Window
{
	[Export] public Label StatusLabel;
	[Export] public ProgressBar ProgressBar;
	[Export] public Button DownloadButton;
	[Export] public Button SkipButton;
	
	private const string AssetManifestUrl = "https://raw.githubusercontent.com/ForestWolf99/SimplyRemadeAssets/refs/heads/main/simplyRemade.json";
	private string _userDataPath;
	private string _assetsPath;
	private HttpRequest _httpRequest;
	private bool _isDownloading = false;
	private bool _hasInternet = false;
	
	public override void _Ready()
	{
		// Get UI elements from scene
		StatusLabel = GetNode<Label>("MarginContainer/VBoxContainer/StatusLabel");
		ProgressBar = GetNode<ProgressBar>("MarginContainer/VBoxContainer/ProgressBar");
		DownloadButton = GetNode<Button>("MarginContainer/VBoxContainer/ButtonContainer/DownloadButton");
		SkipButton = GetNode<Button>("MarginContainer/VBoxContainer/ButtonContainer/SkipButton");
		
		// Set up paths
		_userDataPath = OS.GetUserDataDir();
		_assetsPath = Path.Combine(_userDataPath, "data");
		
		// Ensure data directory exists
		if (!Directory.Exists(_assetsPath))
		{
			Directory.CreateDirectory(_assetsPath);
		}
		
		// Set up window properties
		Title = "Asset Downloader";
		Size = new Vector2I(500, 200);
		Borderless = false;
		AlwaysOnTop = true;
		Unresizable = true;
		
		// Center the window
		Position = (DisplayServer.ScreenGetSize() - Size) / 2;
		
		// Set up HTTP request
		_httpRequest = new HttpRequest();
		AddChild(_httpRequest);
		_httpRequest.RequestCompleted += OnHttpRequestCompleted;
		
		// Connect button signals
		if (DownloadButton != null)
			DownloadButton.Pressed += OnDownloadButtonPressed;
		if (SkipButton != null)
			SkipButton.Pressed += OnSkipButtonPressed;
		
		// Update status
		UpdateStatus("Ready to download assets", 0);
		
		// Auto-check for updates
		CallDeferred("CheckForUpdates");
	}
	
	private async void CheckForUpdates()
	{
		UpdateStatus("Checking for asset updates...", 0);
		
		// Check if we need to download
		var versionFile = Path.Combine(_assetsPath, "version.txt");
		bool hasAssets = File.Exists(versionFile);
		
		// Check internet connectivity
		_hasInternet = await CheckInternetConnectivity();
		
		// If no assets exist and no internet, show error
		if (!hasAssets && !_hasInternet)
		{
			UpdateStatus("ERROR: No assets found and no internet connection. Please connect to the internet to download assets.", 0);
			DownloadButton.Disabled = true;
			SkipButton.Disabled = true;
			return;
		}
		
		// If assets exist and no internet, auto-skip
		if (hasAssets && !_hasInternet)
		{
			UpdateStatus("No internet connection detected. Loading existing assets...", 0);
			await Task.Delay(500);
			await LoadMinecraftJsonFiles();
			CloseAndLoadMainScene();
			return;
		}
		
		// If we have internet and assets exist, check if they're up to date
		if (hasAssets && _hasInternet)
		{
			bool isLatest = await CheckIfLatestVersion();
			if (isLatest)
			{
				UpdateStatus("Latest assets already downloaded. Loading...", 0);
				await Task.Delay(500);
				await LoadMinecraftJsonFiles();
				CloseAndLoadMainScene();
				return;
			}
			else
			{
				UpdateStatus("Asset update available. Click Download to update or Skip to continue.", 0);
			}
		}
		else
		{
			// No assets, but we have internet
			UpdateStatus("Assets not found. Click Download to get the latest assets.", 0);
		}
	}
	
	private async Task<bool> CheckInternetConnectivity()
	{
		try
		{
			var request = new HttpRequest();
			AddChild(request);
			
			bool hasConnection = false;
			bool requestCompleted = false;
			
			request.RequestCompleted += (result, responseCode, headers, body) =>
			{
				hasConnection = (responseCode == 200);
				requestCompleted = true;
			};
			
			// Try to connect to a reliable endpoint
			var error = request.Request("https://www.google.com");
			if (error != Error.Ok)
			{
				request.QueueFree();
				return false;
			}
			
			// Wait for completion with short timeout
			int timeout = 5000; // 5 seconds timeout
			int elapsed = 0;
			while (!requestCompleted && elapsed < timeout)
			{
				await Task.Delay(100);
				elapsed += 100;
			}
			
			request.QueueFree();
			return hasConnection;
		}
		catch (Exception ex)
		{
			GD.Print($"Internet connectivity check failed: {ex.Message}");
			return false;
		}
	}
	
	private async Task<bool> CheckIfLatestVersion()
	{
		try
		{
			var request = new HttpRequest();
			AddChild(request);
			
			string remoteVersion = null;
			bool requestCompleted = false;
			
			request.RequestCompleted += (result, responseCode, headers, body) =>
			{
				if (responseCode == 200)
				{
					try
					{
						var jsonString = System.Text.Encoding.UTF8.GetString(body);
						var assetManifest = JsonSerializer.Deserialize<AssetManifest>(jsonString);
						remoteVersion = assetManifest?.Version;
					}
					catch (Exception ex)
					{
						GD.PrintErr($"Error parsing remote manifest: {ex.Message}");
					}
				}
				requestCompleted = true;
			};
			
			var error = request.Request(AssetManifestUrl);
			if (error != Error.Ok)
			{
				request.QueueFree();
				return false;
			}
			
			// Wait for completion
			int timeout = 10000; // 10 seconds timeout
			int elapsed = 0;
			while (!requestCompleted && elapsed < timeout)
			{
				await Task.Delay(100);
				elapsed += 100;
			}
			
			request.QueueFree();
			
			// Compare versions
			if (remoteVersion != null)
			{
				var versionFile = Path.Combine(_assetsPath, "version.txt");
				if (File.Exists(versionFile))
				{
					var localVersion = File.ReadAllText(versionFile).Trim();
					return localVersion == remoteVersion;
				}
			}
			
			return false;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error checking version: {ex.Message}");
			return false;
		}
	}
	
	private async void OnDownloadButtonPressed()
	{
		if (_isDownloading)
			return;
		
		// Check internet connectivity before downloading
		if (!_hasInternet)
		{
			UpdateStatus("No internet connection. Cannot download assets.", 0);
			return;
		}
			
		_isDownloading = true;
		if (DownloadButton != null)
			DownloadButton.Disabled = true;
		if (SkipButton != null)
			SkipButton.Disabled = true;
		
		await DownloadAssets();
	}
	
	private async void OnSkipButtonPressed()
	{
		await LoadMinecraftJsonFiles();
		CloseAndLoadMainScene();
	}
	
	private Task DownloadAssets()
	{
		try
		{
			UpdateStatus("Downloading asset manifest...", 10);
			
			// Download the JSON manifest
			var error = _httpRequest.Request(AssetManifestUrl);
			if (error != Error.Ok)
			{
				UpdateStatus($"Error requesting manifest: {error}", 0);
				ResetButtons();
			}
			
			// Wait for the request to complete (handled in OnHttpRequestCompleted)
		}
		catch (Exception ex)
		{
			UpdateStatus($"Error: {ex.Message}", 0);
			ResetButtons();
		}
		
		return Task.CompletedTask;
	}
	
	private void OnHttpRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
	{
		if (responseCode != 200)
		{
			UpdateStatus($"Failed to download manifest. Response code: {responseCode}", 0);
			ResetButtons();
			return;
		}
		
		try
		{
			UpdateStatus("Parsing asset manifest...", 20);
			
			var jsonString = System.Text.Encoding.UTF8.GetString(body);
			var assetManifest = JsonSerializer.Deserialize<AssetManifest>(jsonString);
			
			if (assetManifest == null || assetManifest.Assets == null || assetManifest.Assets.Count == 0)
			{
				UpdateStatus("No assets found in manifest.", 0);
				ResetButtons();
				return;
			}
			
			// Download each asset (fire and forget)
			_ = DownloadAssetsFromManifest(assetManifest);
		}
		catch (Exception ex)
		{
			UpdateStatus($"Error parsing manifest: {ex.Message}", 0);
			ResetButtons();
		}
	}
	
	private async Task DownloadAssetsFromManifest(AssetManifest manifest)
	{
		try
		{
			int totalAssets = manifest.Assets.Count;
			int currentAsset = 0;
			
			foreach (var asset in manifest.Assets)
			{
				currentAsset++;
				var progress = 20 + (int)((currentAsset / (float)totalAssets) * 60);
				UpdateStatus($"Downloading {asset.Name} ({currentAsset}/{totalAssets})...", progress);
				
				// Download the asset
				var assetData = await DownloadFileAsync(asset.Url);
				if (assetData == null)
				{
					GD.PrintErr($"Failed to download asset: {asset.Name}");
					continue;
				}
				
				// Save the asset
				var assetFilePath = Path.Combine(_assetsPath, asset.FileName);
				File.WriteAllBytes(assetFilePath, assetData);
				
				// Extract if it's a zip file
				if (asset.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				{
					UpdateStatus($"Extracting {asset.Name}...", progress + 5);
					ExtractZipFile(assetFilePath, _assetsPath);
					
					// Optionally delete the zip file after extraction
					if (asset.DeleteAfterExtract)
					{
						File.Delete(assetFilePath);
					}
				}
			}
			
			// Save version information
			var versionFile = Path.Combine(_assetsPath, "version.txt");
			File.WriteAllText(versionFile, manifest.Version ?? DateTime.Now.ToString());
			
			UpdateStatus("Assets downloaded successfully!", 100);
			
			// Load Minecraft JSON files
			await LoadMinecraftJsonFiles();
			
			// Wait a moment before closing
			await Task.Delay(1000);
			CloseAndLoadMainScene();
		}
		catch (Exception ex)
		{
			UpdateStatus($"Error downloading assets: {ex.Message}", 0);
			ResetButtons();
		}
	}
	
	private async Task<byte[]> DownloadFileAsync(string url)
	{
		try
		{
			var request = new HttpRequest();
			AddChild(request);
			
			byte[] resultData = null;
			bool requestCompleted = false;
			
			request.RequestCompleted += (result, responseCode, headers, body) =>
			{
				if (responseCode == 200)
				{
					resultData = body;
				}
				requestCompleted = true;
			};
			
			var error = request.Request(url);
			if (error != Error.Ok)
			{
				request.QueueFree();
				return null;
			}
			
			// Wait for completion
			int timeout = 30000; // 30 seconds timeout
			int elapsed = 0;
			while (!requestCompleted && elapsed < timeout)
			{
				await Task.Delay(100);
				elapsed += 100;
			}
			
			request.QueueFree();
			return resultData;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error downloading file from {url}: {ex.Message}");
			return null;
		}
	}
	
	private void ExtractZipFile(string zipFilePath, string extractPath)
	{
		try
		{
			ZipFile.ExtractToDirectory(zipFilePath, extractPath, true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error extracting zip file: {ex.Message}");
		}
	}
	
	private void UpdateStatus(string message, int progress)
	{
		if (StatusLabel != null)
			StatusLabel.Text = message;
		if (ProgressBar != null)
			ProgressBar.Value = progress;
		
		GD.Print($"Asset Downloader: {message} ({progress}%)");
	}
	
	private void ResetButtons()
	{
		_isDownloading = false;
		if (DownloadButton != null)
			DownloadButton.Disabled = false;
		if (SkipButton != null)
			SkipButton.Disabled = false;
	}
	
	private async Task LoadMinecraftJsonFiles()
	{
		try
		{
			UpdateStatus("Loading Minecraft JSON files...", 0);
			
			var loader = MinecraftJsonLoader.Instance;
			var success = await loader.LoadAllJsonFiles((message, progress) =>
			{
				UpdateStatus(message, (int)progress);
			});
			
			if (success)
			{
				UpdateStatus($"Loaded {loader.TotalFilesLoaded} Minecraft JSON files successfully!", 100);
				GD.Print($"Minecraft JSON loading complete. Models: {loader.GetAllModelPaths().Count()}, BlockStates: {loader.GetAllBlockStatePaths().Count()}");
			}
			else
			{
				UpdateStatus("Failed to load some Minecraft JSON files. Check the console for details.", 100);
				GD.PrintErr($"Failed files: {loader.TotalFilesFailed}");
			}
			
			await Task.Delay(500);
		}
		catch (Exception ex)
		{
			UpdateStatus($"Error loading JSON files: {ex.Message}", 0);
			GD.PrintErr($"Error in LoadMinecraftJsonFiles: {ex.Message}");
			GD.PrintErr($"Stack trace: {ex.StackTrace}");
		}
	}
	
	private void CloseAndLoadMainScene()
	{
		// Hide this window
		Hide();
		
		// Load the main scene
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
}

// Asset manifest data structures
public class AssetManifest
{
	public string Version { get; set; }
	public List<Asset> Assets { get; set; }
}

public class Asset
{
	public string Name { get; set; }
	public string Url { get; set; }
	public string FileName { get; set; }
	public bool DeleteAfterExtract { get; set; }
}
