using Godot;
using simplyRemadeNuxi;
using simplyRemadeNuxi.core;
using System;
using System.IO;

namespace simplyRemadeNuxi.ui;

/// <summary>
/// Full-screen project selection overlay shown when the application starts.
/// Blocks all input to the main scene until the user creates or opens a project.
///
/// Layout (top → bottom):
///   ┌─────────────────────────────────────────────────────┐
///   │  [Splash image area – TextureRect]                  │
///   ├─────────────────────────────────────────────────────┤
///   │  Title label                                        │
///   │  [New Project]  [Open Project]                      │
///   ├─────────────────────────────────────────────────────┤
///   │  "Recent Projects"                                  │
///   │  ┌──────────────────────────────────────────────┐   │
///   │  │  ScrollContainer → VBoxContainer of buttons  │   │
///   │  └──────────────────────────────────────────────┘   │
///   └─────────────────────────────────────────────────────┘
/// </summary>
public partial class ProjectScreen : Control
{
	// ── Signals ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Emitted when the user has chosen a project (new or opened).
	/// The overlay should be hidden / freed after this signal.
	/// </summary>
	[Signal]
	public delegate void ProjectChosenEventHandler();

	// ── Exported properties ───────────────────────────────────────────────────

	/// <summary>Optional splash image displayed at the top of the screen.</summary>
	[Export] public Texture2D SplashTexture;

	// ── Private nodes ─────────────────────────────────────────────────────────

	private TextureRect _splashRect;
	private VBoxContainer _recentList;
	private Label _noRecentLabel;

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()
	{
		// This control must cover the entire viewport and eat all input so the
		// main scene cannot be interacted with while the overlay is visible.
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildUI();
		RefreshRecentProjects();
	}

	// ── UI construction ───────────────────────────────────────────────────────

	private void BuildUI()
	{
		// ── Dim background ────────────────────────────────────────────────────
		var bg = new ColorRect();
		bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		bg.Color = new Color(0.08f, 0.08f, 0.10f, 0.97f);
		bg.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(bg);

		// ── Centred card ──────────────────────────────────────────────────────
		// CenterContainer fills the full rect and centres its single child.
		var centreContainer = new CenterContainer();
		centreContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		centreContainer.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(centreContainer);

		var card = new PanelContainer();
		card.CustomMinimumSize = new Vector2(560, 0);
		// Style the card
		var cardStyle = new StyleBoxFlat();
		cardStyle.BgColor = new Color(0.14f, 0.14f, 0.17f, 1f);
		cardStyle.CornerRadiusTopLeft     = 12;
		cardStyle.CornerRadiusTopRight    = 12;
		cardStyle.CornerRadiusBottomLeft  = 12;
		cardStyle.CornerRadiusBottomRight = 12;
		cardStyle.BorderColor = new Color(0.30f, 0.30f, 0.40f, 1f);
		cardStyle.BorderWidthLeft   = 1;
		cardStyle.BorderWidthRight  = 1;
		cardStyle.BorderWidthTop    = 1;
		cardStyle.BorderWidthBottom = 1;
		cardStyle.ContentMarginLeft   = 0;
		cardStyle.ContentMarginRight  = 0;
		cardStyle.ContentMarginTop    = 0;
		cardStyle.ContentMarginBottom = 0;
		card.AddThemeStyleboxOverride("panel", cardStyle);
		centreContainer.AddChild(card);

		var outerVBox = new VBoxContainer();
		outerVBox.AddThemeConstantOverride("separation", 0);
		card.AddChild(outerVBox);

		// ── Splash image ──────────────────────────────────────────────────────
		_splashRect = new TextureRect();
		_splashRect.CustomMinimumSize = new Vector2(560, 180);
		_splashRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_splashRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_splashRect.ClipContents = true;
		if (SplashTexture != null)
			_splashRect.Texture = SplashTexture;
		else
		{
			// Placeholder gradient when no splash texture is set
			var grad = new GradientTexture2D();
			var g = new Gradient();
			g.SetColor(0, new Color(0.18f, 0.18f, 0.28f));
			g.SetColor(1, new Color(0.10f, 0.10f, 0.16f));
			grad.Gradient = g;
			grad.Width  = 560;
			grad.Height = 180;
			_splashRect.Texture = grad;
		}
		outerVBox.AddChild(_splashRect);

		// ── Inner padding ─────────────────────────────────────────────────────
		var innerMargin = new MarginContainer();
		innerMargin.AddThemeConstantOverride("margin_left",   28);
		innerMargin.AddThemeConstantOverride("margin_right",  28);
		innerMargin.AddThemeConstantOverride("margin_top",    24);
		innerMargin.AddThemeConstantOverride("margin_bottom", 28);
		outerVBox.AddChild(innerMargin);

		var innerVBox = new VBoxContainer();
		innerVBox.AddThemeConstantOverride("separation", 16);
		innerMargin.AddChild(innerVBox);

		// ── Title ─────────────────────────────────────────────────────────────
		var title = new Label();
		title.Text = "Mine Imator Simply Remade: Nuxi";
		title.AddThemeFontSizeOverride("font_size", 22);
		title.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 1.0f));
		title.HorizontalAlignment = HorizontalAlignment.Center;
		innerVBox.AddChild(title);

		var subtitle = new Label();
		subtitle.Text = "Create a new project or open an existing one to get started.";
		subtitle.AddThemeFontSizeOverride("font_size", 13);
		subtitle.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.75f));
		subtitle.HorizontalAlignment = HorizontalAlignment.Center;
		subtitle.AutowrapMode = TextServer.AutowrapMode.Word;
		innerVBox.AddChild(subtitle);

		// ── Action buttons ────────────────────────────────────────────────────
		var buttonRow = new HBoxContainer();
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		buttonRow.AddThemeConstantOverride("separation", 12);
		innerVBox.AddChild(buttonRow);

		var newBtn = MakeActionButton("＋  New Project", new Color(0.25f, 0.55f, 0.95f));
		newBtn.Pressed += OnNewProjectPressed;
		buttonRow.AddChild(newBtn);

		var openBtn = MakeActionButton("📂  Open Project", new Color(0.30f, 0.30f, 0.40f));
		openBtn.Pressed += OnOpenProjectPressed;
		buttonRow.AddChild(openBtn);

		// ── Separator ─────────────────────────────────────────────────────────
		var sep = new HSeparator();
		innerVBox.AddChild(sep);

		// ── Recent projects ───────────────────────────────────────────────────
		var recentHeader = new Label();
		recentHeader.Text = "Recent Projects";
		recentHeader.AddThemeFontSizeOverride("font_size", 14);
		recentHeader.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.85f));
		innerVBox.AddChild(recentHeader);

		var scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(0, 200);
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		innerVBox.AddChild(scroll);

		_recentList = new VBoxContainer();
		_recentList.AddThemeConstantOverride("separation", 4);
		_recentList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(_recentList);

		_noRecentLabel = new Label();
		_noRecentLabel.Text = "No recent projects.";
		_noRecentLabel.AddThemeColorOverride("font_color", new Color(0.50f, 0.50f, 0.60f));
		_noRecentLabel.AddThemeFontSizeOverride("font_size", 13);
		_noRecentLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_noRecentLabel.Visible = false;
		_recentList.AddChild(_noRecentLabel);
	}

	// ── Recent projects list ──────────────────────────────────────────────────

	/// <summary>Clears and repopulates the recent projects scroll list.</summary>
	public void RefreshRecentProjects()
	{
		// Remove all children except the "no recent" label
		foreach (var child in _recentList.GetChildren())
		{
			if (child != _noRecentLabel)
				child.QueueFree();
		}

		var recents = ProjectManager.GetRecentProjects();

		if (recents.Count == 0)
		{
			_noRecentLabel.Visible = true;
			return;
		}

		_noRecentLabel.Visible = false;

		foreach (var entry in recents)
		{
			var row = BuildRecentRow(entry);
			_recentList.AddChild(row);
		}
	}

	private Control BuildRecentRow(RecentProjectEntry entry)
	{
		// Use a PanelContainer for the styled background + a VBoxContainer for
		// the two-line content.  A transparent full-rect Button sits on top to
		// capture clicks without interfering with the label layout.
		var capturedPath = entry.FilePath;

		var panel = new PanelContainer();
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var normalStyle = new StyleBoxFlat();
		normalStyle.BgColor = new Color(0.18f, 0.18f, 0.22f, 1f);
		normalStyle.CornerRadiusTopLeft     = 6;
		normalStyle.CornerRadiusTopRight    = 6;
		normalStyle.CornerRadiusBottomLeft  = 6;
		normalStyle.CornerRadiusBottomRight = 6;
		normalStyle.ContentMarginLeft   = 12;
		normalStyle.ContentMarginRight  = 12;
		normalStyle.ContentMarginTop    = 8;
		normalStyle.ContentMarginBottom = 8;
		panel.AddThemeStyleboxOverride("panel", normalStyle);

		// Labels inside a margin container
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left",   12);
		margin.AddThemeConstantOverride("margin_right",  12);
		margin.AddThemeConstantOverride("margin_top",     8);
		margin.AddThemeConstantOverride("margin_bottom",  8);
		margin.MouseFilter = MouseFilterEnum.Ignore;
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 2);
		vbox.MouseFilter = MouseFilterEnum.Ignore;
		margin.AddChild(vbox);

		var nameLabel = new Label();
		nameLabel.Text = entry.ProjectName;
		nameLabel.AddThemeFontSizeOverride("font_size", 14);
		nameLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 1.0f));
		nameLabel.MouseFilter = MouseFilterEnum.Ignore;
		nameLabel.ClipText = true;
		vbox.AddChild(nameLabel);

		var pathLabel = new Label();
		pathLabel.Text = entry.FilePath;
		pathLabel.AddThemeFontSizeOverride("font_size", 11);
		pathLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
		pathLabel.MouseFilter = MouseFilterEnum.Ignore;
		pathLabel.ClipText = true;
		vbox.AddChild(pathLabel);

		// Transparent full-rect button overlay to capture hover + click
		var btn = new Button();
		btn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		btn.Flat = true;
		btn.Text = "";

		// All button states fully transparent — visual feedback is done by
		// swapping the PanelContainer's stylebox on mouse enter/exit/press.
		var transparentStyle = new StyleBoxEmpty();
		btn.AddThemeStyleboxOverride("normal",   transparentStyle);
		btn.AddThemeStyleboxOverride("hover",    transparentStyle);
		btn.AddThemeStyleboxOverride("pressed",  transparentStyle);
		btn.AddThemeStyleboxOverride("focus",    transparentStyle);
		btn.AddThemeStyleboxOverride("disabled", transparentStyle);

		var hoverStyle = normalStyle.Duplicate() as StyleBoxFlat;
		hoverStyle.BgColor = new Color(0.24f, 0.24f, 0.32f, 1f);

		var pressStyle = normalStyle.Duplicate() as StyleBoxFlat;
		pressStyle.BgColor = new Color(0.20f, 0.20f, 0.28f, 1f);

		btn.MouseEntered += () => panel.AddThemeStyleboxOverride("panel", hoverStyle);
		btn.MouseExited  += () => panel.AddThemeStyleboxOverride("panel", normalStyle);
		btn.ButtonDown   += () => panel.AddThemeStyleboxOverride("panel", pressStyle);
		btn.ButtonUp     += () => panel.AddThemeStyleboxOverride("panel", hoverStyle);

		btn.Pressed += () => OnRecentProjectPressed(capturedPath);
		panel.AddChild(btn);

		return panel;
	}

	// ── Button handlers ───────────────────────────────────────────────────────

	private void OnNewProjectPressed()
	{
		// Delegate to Main's existing new-project dialog
		Main.Instance?.ShowNewProjectDialogFromScreen(OnProjectCreatedOrOpened);
	}

	private void OnOpenProjectPressed()
	{
		// Delegate to Main's existing open-project dialog
		Main.Instance?.ShowOpenProjectDialogFromScreen(OnProjectCreatedOrOpened);
	}

	private void OnRecentProjectPressed(string filePath)
	{
		if (!File.Exists(filePath))
		{
			// File no longer exists – remove from list and show a message
			ProjectManager.RemoveFromRecentProjects(filePath);
			RefreshRecentProjects();

			var dlg = new AcceptDialog();
			dlg.Title      = "Project Not Found";
			dlg.DialogText = $"The project file could not be found:\n{filePath}\n\nIt has been removed from the recent projects list.";
			dlg.OkButtonText = "OK";
			dlg.Exclusive  = true;
			dlg.Transient  = true;
			dlg.CloseRequested += () => { dlg.Hide(); dlg.QueueFree(); };
			AddChild(dlg);
			dlg.PopupCentered();
			return;
		}

		if (ProjectManager.OpenProject(filePath))
		{
			Main.Instance?.SetWindowTitle(
				$"Mine Imator Simply Remade: Nuxi — {ProjectManager.CurrentProjectName}");
			_ = Main.Instance?.RestoreSceneWithProgressAsync();
			OnProjectCreatedOrOpened();
		}
	}

	/// <summary>Called after any successful project creation or open action.</summary>
	private void OnProjectCreatedOrOpened()
	{
		EmitSignal(SignalName.ProjectChosen);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static Button MakeActionButton(string text, Color bgColor)
	{
		var btn = new Button();
		btn.Text = text;
		btn.CustomMinimumSize = new Vector2(180, 44);

		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.CornerRadiusTopLeft     = 8;
		style.CornerRadiusTopRight    = 8;
		style.CornerRadiusBottomLeft  = 8;
		style.CornerRadiusBottomRight = 8;
		style.ContentMarginLeft   = 16;
		style.ContentMarginRight  = 16;
		style.ContentMarginTop    = 10;
		style.ContentMarginBottom = 10;
		btn.AddThemeStyleboxOverride("normal", style);

		var hoverStyle = style.Duplicate() as StyleBoxFlat;
		hoverStyle.BgColor = bgColor.Lightened(0.12f);
		btn.AddThemeStyleboxOverride("hover", hoverStyle);

		var pressStyle = style.Duplicate() as StyleBoxFlat;
		pressStyle.BgColor = bgColor.Darkened(0.10f);
		btn.AddThemeStyleboxOverride("pressed", pressStyle);

		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.AddThemeColorOverride("font_color", new Color(1, 1, 1));

		return btn;
	}
}
