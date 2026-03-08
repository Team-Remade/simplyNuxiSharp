using Godot;
using System;

/// <summary>
/// A modal progress window that displays model loading progress.
/// Shows a progress bar and status message during async model loading.
/// </summary>
public partial class ModelLoadingProgressWindow : Window
{
	private Label _statusLabel;
	private ProgressBar _progressBar;
	private Label _titleLabel;
	private string _modelName = "";
	
	/// <summary>
	/// Event fired when the user cancels the loading
	/// </summary>
	public event Action OnCancel;
	
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
		vbox.AddThemeConstantOverride("separation", 15);
		margin.AddChild(vbox);
		
		// Title label
		_titleLabel = new Label();
		_titleLabel.Text = "Loading Model";
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeFontSizeOverride("font_size", 18);
		vbox.AddChild(_titleLabel);
		
		// Status label
		_statusLabel = new Label();
		_statusLabel.Text = "Initializing...";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_statusLabel);
		
		// Progress bar
		_progressBar = new ProgressBar();
		_progressBar.CustomMinimumSize = new Vector2(400, 30);
		_progressBar.ShowPercentage = true;
		_progressBar.MinValue = 0;
		_progressBar.MaxValue = 100;
		_progressBar.Value = 0;
		vbox.AddChild(_progressBar);
		
		// Set window properties
		Title = "Loading Model";
		Size = new Vector2I(450, 160);
		Borderless = false;
		AlwaysOnTop = true;
		Unresizable = true;
		Exclusive = true;  // Block input to other windows
		
		// Center the window on screen
		CenterWindow();
		
		// Connect to close request for cancellation
		CloseRequested += OnCloseRequested;
	}
	
	/// <summary>
	/// Updates the progress display
	/// </summary>
	/// <param name="progress">Progress value from 0.0 to 1.0</param>
	/// <param name="status">Status message to display</param>
	public void UpdateProgress(float progress, string status)
	{
		if (_statusLabel != null)
			_statusLabel.Text = status;
		
		if (_progressBar != null)
			_progressBar.Value = progress * 100;  // Convert 0-1 to 0-100
	}
	
	/// <summary>
	/// Sets the model name to display in the title
	/// </summary>
	/// <param name="modelName">Name of the model being loaded</param>
	public void SetModelName(string modelName)
	{
		_modelName = modelName;
		if (_titleLabel != null && !string.IsNullOrEmpty(modelName))
		{
			_titleLabel.Text = $"Loading Model: {modelName}";
		}
	}
	
	/// <summary>
	/// Shows the window and resets progress
	/// </summary>
	public void ShowWindow()
	{
		UpdateProgress(0, "Starting...");
		Show();
		// Ensure it's on top and focused
		GrabFocus();
	}
	
	/// <summary>
	/// Hides the window
	/// </summary>
	public void HideWindow()
	{
		Hide();
	}
	
	private void OnCloseRequested()
	{
		// User clicked X button - treat as cancel
		OnCancel?.Invoke();
		HideWindow();
	}
	
	private void CenterWindow()
	{
		var screenSize = DisplayServer.ScreenGetSize();
		Position = (screenSize - Size) / 2;
	}
}
