using Godot;
using System;

namespace simplyRemadeNuxi.ui;

/// <summary>
/// Simple loading window to show progress during JSON file loading
/// </summary>
public partial class JsonLoadingWindow : Window
{
	private Label _statusLabel;
	private ProgressBar _progressBar;
	
	public override void _Ready()
	{
		// Create UI elements programmatically
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		AddChild(margin);
		
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 10);
		margin.AddChild(vbox);
		
		// Title
		var title = new Label();
		title.Text = "Loading Minecraft Assets";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 18);
		vbox.AddChild(title);
		
		// Status label
		_statusLabel = new Label();
		_statusLabel.Text = "Initializing...";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_statusLabel);
		
		// Progress bar
		_progressBar = new ProgressBar();
		_progressBar.CustomMinimumSize = new Vector2(400, 30);
		_progressBar.ShowPercentage = true;
		vbox.AddChild(_progressBar);
		
		// Set window properties
		Title = "Loading Assets";
		Size = new Vector2I(450, 150);
		Borderless = false;
		AlwaysOnTop = true;
		Unresizable = true;
		
		// Center the window
		Position = (DisplayServer.ScreenGetSize() - Size) / 2;
	}
	
	public void UpdateProgress(string status, float progress)
	{
		if (_statusLabel != null)
			_statusLabel.Text = status;
		if (_progressBar != null)
			_progressBar.Value = progress;
	}
}
