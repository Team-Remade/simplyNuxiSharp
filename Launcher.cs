using Godot;

namespace simplyRemadeNuxi;

//𖥂
public partial class Launcher : Node
{
	public override void _Ready()
	{
		// Load and instantiate the AssetDownloaderWindow scene
		var assetDownloaderScene = GD.Load<PackedScene>("res://AssetDownloaderWindow.tscn");
		var assetDownloaderWindow = assetDownloaderScene.Instantiate<Window>();
		
		// Add the window to the scene tree
		AddChild(assetDownloaderWindow);
		
		// Show the window
		assetDownloaderWindow.Show();
	}
}
