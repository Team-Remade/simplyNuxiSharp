using Godot;

namespace simplyRemadeNuxi.ui;

/// <summary>
/// A lightweight, non-blocking toast notification that slides in from the
/// bottom-right corner of the screen, stays visible for a moment, then fades
/// out and removes itself.
///
/// Usage (from any node that has access to the scene tree root):
/// <code>
///   ToastNotification.Show(GetTree().Root, "Project saved!");
/// </code>
/// </summary>
public partial class ToastNotification : Control
{
	// ── Configuration ────────────────────────────────────────────────────────

	/// <summary>How long (seconds) the toast stays fully visible.</summary>
	private const float HoldDuration = 2.0f;

	/// <summary>Duration (seconds) of the fade-in slide animation.</summary>
	private const float FadeInDuration = 0.25f;

	/// <summary>Duration (seconds) of the fade-out animation.</summary>
	private const float FadeOutDuration = 0.4f;

	/// <summary>Horizontal distance (px) the panel slides in from the right.</summary>
	private const float SlideDistance = 40f;

	/// <summary>Margin from the right/bottom edges of the viewport (px).</summary>
	private const float EdgeMargin = 20f;

	// ── State ────────────────────────────────────────────────────────────────

	private enum Phase { FadeIn, Hold, FadeOut }

	private Phase _phase = Phase.FadeIn;
	private float _timer = 0f;

	private Panel _panel;
	private Label _label;

	// ── Factory ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Creates and adds a toast notification to the given parent node.
	/// The toast removes itself automatically when it finishes.
	/// </summary>
	/// <param name="parent">The node to attach the toast to (typically the scene root or a CanvasLayer).</param>
	/// <param name="message">The text to display.</param>
	/// <param name="icon">Optional icon character/emoji prepended to the message.</param>
	public static ToastNotification Show(Node parent, string message, string icon = "✔")
	{
		var toast = new ToastNotification();
		toast._message = string.IsNullOrEmpty(icon) ? message : $"{icon}  {message}";
		parent.AddChild(toast);
		return toast;
	}

	private string _message = "";

	// ── Lifecycle ────────────────────────────────────────────────────────────

	private StyleBoxFlat _style;
	private bool _initialized = false;

	public override void _Ready()
	{
		// This control should sit on top of everything and not intercept input.
		MouseFilter = MouseFilterEnum.Ignore;
		AnchorsPreset = (int)LayoutPreset.FullRect;

		// ── Panel (background) ───────────────────────────────────────────────
		_panel = new Panel();
		_panel.MouseFilter = MouseFilterEnum.Ignore;

		// Style the panel: dark semi-transparent rounded rectangle
		_style = new StyleBoxFlat();
		_style.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.92f);
		_style.CornerRadiusTopLeft     = 8;
		_style.CornerRadiusTopRight    = 8;
		_style.CornerRadiusBottomLeft  = 8;
		_style.CornerRadiusBottomRight = 8;
		_style.ContentMarginLeft   = 18;
		_style.ContentMarginRight  = 18;
		_style.ContentMarginTop    = 10;
		_style.ContentMarginBottom = 10;
		// Subtle border
		_style.BorderColor = new Color(0.35f, 0.35f, 0.35f, 0.8f);
		_style.BorderWidthLeft   = 1;
		_style.BorderWidthRight  = 1;
		_style.BorderWidthTop    = 1;
		_style.BorderWidthBottom = 1;
		_panel.AddThemeStyleboxOverride("panel", _style);

		AddChild(_panel);

		// ── Label ────────────────────────────────────────────────────────────
		_label = new Label();
		_label.Text = _message;
		_label.MouseFilter = MouseFilterEnum.Ignore;
		_label.AddThemeFontSizeOverride("font_size", 14);
		_label.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		_label.AutowrapMode = TextServer.AutowrapMode.Off;
		_panel.AddChild(_label);

		// Begin fully transparent; sizing is deferred to the first _Process frame
		// so that the label's minimum size has been computed by the layout engine.
		Modulate = new Color(1, 1, 1, 0);
	}

	public override void _Process(double delta)
	{
		// On the very first frame after _Ready, the label's minimum size is now
		// available from the layout engine.  Use it to size the panel correctly.
		if (!_initialized)
		{
			_initialized = true;
			var labelMin = _label.GetMinimumSize();
			var panelSize = new Vector2(
				labelMin.X + _style.ContentMarginLeft + _style.ContentMarginRight,
				labelMin.Y + _style.ContentMarginTop  + _style.ContentMarginBottom
			);
			_panel.Size = panelSize;
			RepositionPanel(1f); // start fully slid out (off-screen to the right)
		}

		_timer += (float)delta;

		switch (_phase)
		{
			case Phase.FadeIn:
			{
				float t = Mathf.Clamp(_timer / FadeInDuration, 0f, 1f);
				Modulate = new Color(1, 1, 1, t);
				RepositionPanel(1f - t); // slide in from right
				if (_timer >= FadeInDuration)
				{
					_phase = Phase.Hold;
					_timer = 0f;
					Modulate = new Color(1, 1, 1, 1);
					RepositionPanel(0f);
				}
				break;
			}

			case Phase.Hold:
			{
				if (_timer >= HoldDuration)
				{
					_phase = Phase.FadeOut;
					_timer = 0f;
				}
				break;
			}

			case Phase.FadeOut:
			{
				float t = Mathf.Clamp(_timer / FadeOutDuration, 0f, 1f);
				Modulate = new Color(1, 1, 1, 1f - t);
				if (_timer >= FadeOutDuration)
				{
					QueueFree();
				}
				break;
			}
		}
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Positions the panel in the bottom-right corner.
	/// <paramref name="slideT"/> = 0 means fully in position; 1 means fully slid off to the right.
	/// </summary>
	private void RepositionPanel(float slideT)
	{
		if (_panel == null) return;

		var viewportSize = GetViewportRect().Size;
		float x = viewportSize.X - _panel.Size.X - EdgeMargin + slideT * SlideDistance;
		float y = viewportSize.Y - _panel.Size.Y - EdgeMargin;
		_panel.Position = new Vector2(x, y);
	}
}
