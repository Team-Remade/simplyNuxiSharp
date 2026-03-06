using Godot;
using FFMpegCore;
using FFMpegCore.Extensions.Downloader;

namespace simplyRemadeNuxi;

//𖥂
public partial class Launcher : Node
{
	private Window _loadingWindow;
	private Label _loadingLabel;
	
	public override async void _Ready()
	{
		// Create and show loading window
		ShowLoadingWindow("Checking FFMpeg binaries...");
		
		// Download FFMpeg binaries if not present
		try
		{
			// Set the binary folder path for FFMpeg in user data directory
			var ffmpegPath = System.IO.Path.Combine(OS.GetUserDataDir(), "ffmpeg");
			System.IO.Directory.CreateDirectory(ffmpegPath);
			GlobalFFOptions.Configure(options => options.BinaryFolder = ffmpegPath);
			
			// Check if FFmpeg is available by trying to execute it
			bool ffmpegAvailable = false;
			try
			{
				// Try to get FFmpeg version using a simple command
				var process = new System.Diagnostics.Process();
				process.StartInfo.FileName = GlobalFFOptions.GetFFMpegBinaryPath();
				process.StartInfo.Arguments = "-version";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				process.WaitForExit();
				
				if (process.ExitCode == 0)
				{
					UpdateLoadingWindow("FFMpeg binaries found");
					ffmpegAvailable = true;
				}
			}
			catch
			{
				// FFmpeg not available, need to download
				ffmpegAvailable = false;
			}
			
			if (!ffmpegAvailable)
			{
				UpdateLoadingWindow("Downloading FFMpeg binaries...");
				await FFMpegDownloader.DownloadBinaries();
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Failed to download FFMpeg: {ex.Message}");
			UpdateLoadingWindow($"Error: {ex.Message}");
			await ToSignal(GetTree().CreateTimer(3.0), "timeout");
		}
		
		// Close loading window
		CloseLoadingWindow();
		
		// Load and instantiate the AssetDownloaderWindow scene
		var assetDownloaderScene = GD.Load<PackedScene>("res://AssetDownloaderWindow.tscn");
		var assetDownloaderWindow = assetDownloaderScene.Instantiate<Window>();
		
		// Add the window to the scene tree
		AddChild(assetDownloaderWindow);
		
		// Show the window
		assetDownloaderWindow.Show();
	}
	
	private void ShowLoadingWindow(string message)
	{
		_loadingWindow = new Window();
		_loadingWindow.Title = "Loading";
		_loadingWindow.Size = new Vector2I(400, 150);
		_loadingWindow.Unresizable = true;
		_loadingWindow.Borderless = false;
		_loadingWindow.AlwaysOnTop = true;
		
		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		vbox.AddThemeConstantOverride("separation", 20);
		
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		
		_loadingLabel = new Label();
		_loadingLabel.Text = message;
		_loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_loadingLabel.VerticalAlignment = VerticalAlignment.Center;
		
		vbox.AddChild(_loadingLabel);
		margin.AddChild(vbox);
		_loadingWindow.AddChild(margin);
		
		AddChild(_loadingWindow);
		_loadingWindow.Position = (DisplayServer.ScreenGetSize() / 2) - (_loadingWindow.Size / 2);
		_loadingWindow.Show();
	}
	
	private void UpdateLoadingWindow(string message)
	{
		if (_loadingLabel != null)
		{
			_loadingLabel.Text = message;
		}
	}
	
	private void CloseLoadingWindow()
	{
		if (_loadingWindow != null)
		{
			_loadingWindow.QueueFree();
			_loadingWindow = null;
			_loadingLabel = null;
		}
	}
}
