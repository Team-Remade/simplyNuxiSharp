using Godot;
using System;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.ui;

/// <summary>
/// Dialog for rendering an animation from the preview viewport
/// </summary>
public partial class RenderAnimationDialog : Window
{
	private VBoxContainer _mainContainer;
	private OptionButton _outputTypeDropdown;
	private OptionButton _videoFormatDropdown;
	private Label _resolutionLabel;
	private Label _framerateLabel;
	private Label _durationLabel;
	private SpinBox _bitrateSpinBox;
	private VBoxContainer _videoOptionsContainer;
	private Button _renderButton;
	private Button _cancelButton;
	
	private Action<string, string, bool, int> _onRenderCallback; // (filePath, format, isPngSequence, bitrateMbps)
	
	// Output types
	private readonly string[] _outputTypes = { "Video File", "PNG Sequence" };
	
	// Video formats
	private readonly string[] _videoFormats = { "MP4", "AVI", "MOV", "MKV" };
	private readonly string[] _videoExtensions = { "*.mp4", "*.avi", "*.mov", "*.mkv" };
	
	private int _lastKeyframe = 0;
	private float _framerate = 30f;
	
	public override void _Ready()
	{
		Title = "Render Animation";
		MinSize = new Vector2I(450, 500);
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
		_mainContainer.CustomMinimumSize = new Vector2(410, 0);
		marginContainer.AddChild(_mainContainer);
		
		// Title
		var titleLabel = new Label();
		titleLabel.Text = "Render Animation Settings";
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
		
		// Framerate display
		var framerateContainer = new VBoxContainer();
		_mainContainer.AddChild(framerateContainer);
		
		var framerateTitleLabel = new Label();
		framerateTitleLabel.Text = "Framerate:";
		framerateContainer.AddChild(framerateTitleLabel);
		
		_framerateLabel = new Label();
		_framerateLabel.Text = "30 FPS";
		_framerateLabel.AddThemeFontSizeOverride("font_size", 14);
		_framerateLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		framerateContainer.AddChild(_framerateLabel);
		
		// Duration display
		var durationContainer = new VBoxContainer();
		_mainContainer.AddChild(durationContainer);
		
		var durationTitleLabel = new Label();
		durationTitleLabel.Text = "Duration:";
		durationContainer.AddChild(durationTitleLabel);
		
		_durationLabel = new Label();
		_durationLabel.Text = "0 frames (0.00 seconds)";
		_durationLabel.AddThemeFontSizeOverride("font_size", 14);
		_durationLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		durationContainer.AddChild(_durationLabel);
		
		// Output type selection
		var outputTypeContainer = new VBoxContainer();
		_mainContainer.AddChild(outputTypeContainer);
		
		var outputTypeLabel = new Label();
		outputTypeLabel.Text = "Output Type:";
		outputTypeContainer.AddChild(outputTypeLabel);
		
		_outputTypeDropdown = new OptionButton();
		foreach (var type in _outputTypes)
		{
			_outputTypeDropdown.AddItem(type);
		}
		_outputTypeDropdown.Selected = 0; // Default to Video File
		_outputTypeDropdown.ItemSelected += OnOutputTypeChanged;
		outputTypeContainer.AddChild(_outputTypeDropdown);
		
		// Video options container (shown only for video files)
		_videoOptionsContainer = new VBoxContainer();
		_videoOptionsContainer.AddThemeConstantOverride("separation", 10);
		_mainContainer.AddChild(_videoOptionsContainer);
		
		// Video format selection
		var formatContainer = new VBoxContainer();
		_videoOptionsContainer.AddChild(formatContainer);
		
		var formatLabel = new Label();
		formatLabel.Text = "Video Format:";
		formatContainer.AddChild(formatLabel);
		
		_videoFormatDropdown = new OptionButton();
		foreach (var format in _videoFormats)
		{
			_videoFormatDropdown.AddItem(format);
		}
		_videoFormatDropdown.Selected = 0; // Default to MP4
		formatContainer.AddChild(_videoFormatDropdown);
		
		// Bitrate selection
		var bitrateContainer = new VBoxContainer();
		_videoOptionsContainer.AddChild(bitrateContainer);
		
		var bitrateLabel = new Label();
		bitrateLabel.Text = "Bitrate (Mbps):";
		bitrateContainer.AddChild(bitrateLabel);
		
		_bitrateSpinBox = new SpinBox();
		_bitrateSpinBox.MinValue = 1;
		_bitrateSpinBox.MaxValue = 100;
		_bitrateSpinBox.Value = 10;
		_bitrateSpinBox.Step = 1;
		_bitrateSpinBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		bitrateContainer.AddChild(_bitrateSpinBox);
		
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
	
	private void OnOutputTypeChanged(long index)
	{
		// Show/hide video options based on output type
		_videoOptionsContainer.Visible = (index == 0); // Video File
		
		// Resize window to fit new content
		CallDeferred(MethodName.ResetSize);
	}
	
	public void SetResolution(int width, int height)
	{
		if (_resolutionLabel != null)
		{
			_resolutionLabel.Text = $"{width} x {height}";
		}
	}
	
	public void SetFramerate(float framerate)
	{
		_framerate = framerate;
		if (_framerateLabel != null)
		{
			_framerateLabel.Text = $"{framerate} FPS";
		}
		UpdateDurationLabel();
	}
	
	public void SetLastKeyframe(int lastKeyframe)
	{
		_lastKeyframe = lastKeyframe;
		UpdateDurationLabel();
	}
	
	private void UpdateDurationLabel()
	{
		if (_durationLabel != null && _framerate > 0)
		{
			float durationSeconds = _lastKeyframe / _framerate;
			_durationLabel.Text = $"{_lastKeyframe} frames ({durationSeconds:F2} seconds)";
		}
	}
	
	public void SetRenderCallback(Action<string, string, bool, int> callback)
	{
		_onRenderCallback = callback;
	}
	
	private void OnRenderPressed()
	{
		bool isPngSequence = _outputTypeDropdown.Selected == 1;
		
		if (isPngSequence)
		{
			// For PNG sequence, select a directory
			NativeFileDialog.ShowOpenDirectory(
				"Select Output Directory for PNG Sequence",
				(success, directoryPath) =>
				{
					if (success && !string.IsNullOrEmpty(directoryPath))
					{
						_onRenderCallback?.Invoke(directoryPath, "PNG", true, 0);
						Hide();
						QueueFree();
					}
				}
			);
		}
		else
		{
			// For video file, select a file path
			var selectedFormat = _videoFormats[_videoFormatDropdown.Selected];
			var extension = _videoExtensions[_videoFormatDropdown.Selected];
			var bitrate = (int)_bitrateSpinBox.Value;
			
			NativeFileDialog.ShowSaveFile(
				"Save Rendered Video",
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
						
						_onRenderCallback?.Invoke(filePath, selectedFormat, false, bitrate);
						Hide();
						QueueFree();
					}
				},
				"",
				$"animation.{selectedFormat.ToLower()}"
			);
		}
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
