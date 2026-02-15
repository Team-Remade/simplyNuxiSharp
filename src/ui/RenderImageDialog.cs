using Godot;
using System;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.ui;

/// <summary>
/// Dialog for rendering a single image from the preview viewport
/// </summary>
public partial class RenderImageDialog : Window
{
	private VBoxContainer _mainContainer;
	private OptionButton _formatDropdown;
	private Label _resolutionLabel;
	private Button _renderButton;
	private Button _cancelButton;
	
	private Action<string, string> _onRenderCallback; // (filePath, format)
	
	// Supported image formats
	private readonly string[] _imageFormats = { "PNG", "JPG", "WEBP", "BMP" };
	private readonly string[] _imageExtensions = { "*.png", "*.jpg", "*.webp", "*.bmp" };
	
	public override void _Ready()
	{
		Title = "Render Image";
		MinSize = new Vector2I(400, 200);
		Borderless = false;
		Unresizable = true;
		Transient = true;
		Exclusive = true;
		
		// Handle close request (X button)
		CloseRequested += OnCloseRequested;
		
		SetupUi();
		
		// Dynamically size to content
		CallDeferred(MethodName.AdjustSize);
	}
	
	private void AdjustSize()
	{
		ResetSize();
		PopupCentered();
	}
	
	private void SetupUi()
	{
		// Create a margin container for padding
		var marginContainer = new MarginContainer();
		marginContainer.AddThemeConstantOverride("margin_left", 20);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_right", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		AddChild(marginContainer);
		
		_mainContainer = new VBoxContainer();
		_mainContainer.AddThemeConstantOverride("separation", 10);
		_mainContainer.CustomMinimumSize = new Vector2(360, 0);
		marginContainer.AddChild(_mainContainer);
		
		// Title
		var titleLabel = new Label();
		titleLabel.Text = "Render Image Settings";
		titleLabel.AddThemeFontSizeOverride("font_size", 16);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_mainContainer.AddChild(titleLabel);
		
		// Spacer
		var spacer1 = new Control();
		spacer1.CustomMinimumSize = new Vector2(0, 10);
		_mainContainer.AddChild(spacer1);
		
		// Resolution display
		var resolutionContainer = new VBoxContainer();
		_mainContainer.AddChild(resolutionContainer);
		
		var resolutionTitleLabel = new Label();
		resolutionTitleLabel.Text = "Render Resolution:";
		resolutionContainer.AddChild(resolutionTitleLabel);
		
		_resolutionLabel = new Label();
		_resolutionLabel.Text = "1920 x 1080";
		_resolutionLabel.AddThemeFontSizeOverride("font_size", 14);
		_resolutionLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		resolutionContainer.AddChild(_resolutionLabel);
		
		// Format selection
		var formatContainer = new VBoxContainer();
		_mainContainer.AddChild(formatContainer);
		
		var formatLabel = new Label();
		formatLabel.Text = "Image Format:";
		formatContainer.AddChild(formatLabel);
		
		_formatDropdown = new OptionButton();
		foreach (var format in _imageFormats)
		{
			_formatDropdown.AddItem(format);
		}
		_formatDropdown.Selected = 0; // Default to PNG
		formatContainer.AddChild(_formatDropdown);
		
		// Spacer
		var spacer2 = new Control();
		spacer2.CustomMinimumSize = new Vector2(0, 10);
		spacer2.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_mainContainer.AddChild(spacer2);
		
		// Buttons
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.End;
		buttonContainer.AddThemeConstantOverride("separation", 10);
		_mainContainer.AddChild(buttonContainer);
		
		_cancelButton = new Button();
		_cancelButton.Text = "Cancel";
		_cancelButton.CustomMinimumSize = new Vector2(100, 0);
		_cancelButton.Pressed += OnCancelPressed;
		buttonContainer.AddChild(_cancelButton);
		
		_renderButton = new Button();
		_renderButton.Text = "Render";
		_renderButton.CustomMinimumSize = new Vector2(100, 0);
		_renderButton.Pressed += OnRenderPressed;
		buttonContainer.AddChild(_renderButton);
	}
	
	public void SetResolution(int width, int height)
	{
		if (_resolutionLabel != null)
		{
			_resolutionLabel.Text = $"{width} x {height}";
		}
	}
	
	public void SetRenderCallback(Action<string, string> callback)
	{
		_onRenderCallback = callback;
	}
	
	private void OnRenderPressed()
	{
		var selectedFormat = _imageFormats[_formatDropdown.Selected];
		var extension = _imageExtensions[_formatDropdown.Selected];
		
		// Show native file dialog
		NativeFileDialog.ShowSaveFile(
			"Save Rendered Image",
			new[] { extension },
			(success, filePath) =>
			{
				if (success && !string.IsNullOrEmpty(filePath))
				{
					// Ensure the file has the correct extension
					if (!filePath.EndsWith(selectedFormat.ToLower()))
					{
						filePath += "." + selectedFormat.ToLower();
					}
					
					_onRenderCallback?.Invoke(filePath, selectedFormat);
					Hide();
					QueueFree();
				}
			},
			"",
			$"render.{selectedFormat.ToLower()}"
		);
	}
	
	private void OnCancelPressed()
	{
		Hide();
		QueueFree();
	}
	
	private void OnCloseRequested()
	{
		Hide();
		QueueFree();
	}
}
