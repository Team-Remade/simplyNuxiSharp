using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.core;

/// <summary>
/// The Content Drawer panel.  Displayed in the bottom-centre dockable tab
/// ("Content Drawer") of Main.tscn.
///
/// Provides:
///   • A toolbar with "Import Asset" and "Open in Explorer" buttons.
///   • A tab bar to filter by asset type (All / Models / Images / Audio / Other).
///   • An ItemList showing every asset registered in the current project.
///   • Right-click context menu: Rename label, Remove from project, Delete file.
///
/// The panel listens to ProjectManager events so it refreshes automatically
/// when the project is opened, saved, or assets are added/removed.
/// </summary>
public partial class ContentDrawerPanel : Panel
{
	// ── UI nodes ──────────────────────────────────────────────────────────────

	private Label       _noProjectLabel;
	private VBoxContainer _mainContainer;

	private TabBar      _tabBar;
	private ItemList    _assetList;
	private Label       _statusLabel;

	private PopupMenu   _contextMenu;
	private int         _contextMenuAssetIndex = -1;

	// ── State ─────────────────────────────────────────────────────────────────

	private static readonly string[] TabNames = { "All", "Models", "Images", "Audio", "Other" };
	private int _selectedTab = 0;

	// Maps ItemList index → AssetEntry
	private readonly List<AssetEntry> _displayedAssets = new();

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()
	{
		BuildUi();
		SubscribeToProjectEvents();
		Refresh();
	}

	public override void _ExitTree()
	{
		UnsubscribeFromProjectEvents();
	}

	// ── Event subscriptions ───────────────────────────────────────────────────

	private void SubscribeToProjectEvents()
	{
		ProjectManager.ProjectOpened  += OnProjectChanged;
		ProjectManager.ProjectClosed  += OnProjectClosed;
		ProjectManager.ProjectSaved   += OnProjectSaved;
		ProjectManager.AssetsChanged  += OnAssetsChanged;
	}

	private void UnsubscribeFromProjectEvents()
	{
		ProjectManager.ProjectOpened  -= OnProjectChanged;
		ProjectManager.ProjectClosed  -= OnProjectClosed;
		ProjectManager.ProjectSaved   -= OnProjectSaved;
		ProjectManager.AssetsChanged  -= OnAssetsChanged;
	}

	private void OnProjectChanged(string _) => Refresh();
	private void OnProjectClosed()          => Refresh();
	private void OnProjectSaved()           => UpdateStatusLabel();
	private void OnAssetsChanged()          => RefreshAssetList();

	// ── UI construction ───────────────────────────────────────────────────────

	private void BuildUi()
	{
		// Root fills the panel
		var root = new VBoxContainer();
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddThemeConstantOverride("separation", 4);
		AddChild(root);

		// ── "No project open" placeholder ────────────────────────────────────
		_noProjectLabel = new Label();
		_noProjectLabel.Text = "No project open.\nUse File → New Project or File → Open Project.";
		_noProjectLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_noProjectLabel.VerticalAlignment   = VerticalAlignment.Center;
		_noProjectLabel.SizeFlagsVertical   = SizeFlags.ExpandFill;
		_noProjectLabel.AutowrapMode        = TextServer.AutowrapMode.WordSmart;
		root.AddChild(_noProjectLabel);

		// ── Main content (hidden when no project) ─────────────────────────────
		_mainContainer = new VBoxContainer();
		_mainContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_mainContainer.AddThemeConstantOverride("separation", 4);
		root.AddChild(_mainContainer);

		// Toolbar
		var toolbar = new HBoxContainer();
		toolbar.AddThemeConstantOverride("separation", 6);
		_mainContainer.AddChild(toolbar);

		var titleLabel = new Label();
		titleLabel.Text = "Project Assets";
		titleLabel.AddThemeFontSizeOverride("font_size", 14);
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		toolbar.AddChild(titleLabel);

		var importBtn = new Button();
		importBtn.Text    = "⊕ Import";
		importBtn.TooltipText = "Import an asset file into the project";
		importBtn.Pressed += OnImportButtonPressed;
		toolbar.AddChild(importBtn);

		var explorerBtn = new Button();
		explorerBtn.Text      = "📂 Open Folder";
		explorerBtn.TooltipText = "Open the project assets folder in the file explorer";
		explorerBtn.Pressed   += OnOpenFolderPressed;
		toolbar.AddChild(explorerBtn);

		// Separator
		var sep = new HSeparator();
		_mainContainer.AddChild(sep);

		// Tab bar (filter by type)
		_tabBar = new TabBar();
		_tabBar.TabAlignment = TabBar.AlignmentMode.Left;
		foreach (var name in TabNames)
			_tabBar.AddTab(name);
		_tabBar.CurrentTab = 0;
		_tabBar.TabChanged += OnTabChanged;
		_mainContainer.AddChild(_tabBar);

		// Asset list
		_assetList = new ItemList();
		_assetList.SizeFlagsVertical   = SizeFlags.ExpandFill;
		_assetList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_assetList.SelectMode          = ItemList.SelectModeEnum.Single;
		_assetList.AllowRmbSelect      = true;
		_assetList.ItemClicked         += OnAssetItemClicked;
		_assetList.ItemActivated       += OnAssetItemActivated;  // double-click to spawn
		_mainContainer.AddChild(_assetList);

		// Status bar
		_statusLabel = new Label();
		_statusLabel.Text = "";
		_statusLabel.AddThemeFontSizeOverride("font_size", 11);
		_mainContainer.AddChild(_statusLabel);

		// Context menu — use IdPressed so the handler receives the item ID,
		// not the positional index (which changes when separators are present).
		_contextMenu = new PopupMenu();
		_contextMenu.AddItem("Spawn in Scene",      0);
		_contextMenu.AddItem("Add to Timeline",     4);
		_contextMenu.AddSeparator();
		_contextMenu.AddItem("Rename Label",        1);
		_contextMenu.AddSeparator();
		_contextMenu.AddItem("Remove from Project", 2);
		_contextMenu.AddItem("Delete File",         3);
		_contextMenu.IdPressed += OnContextMenuIndexPressed;
		AddChild(_contextMenu);
	}

	// ── Refresh logic ─────────────────────────────────────────────────────────

	private void Refresh()
	{
		bool hasProject = !string.IsNullOrEmpty(ProjectManager.CurrentProjectFolder);
		_noProjectLabel.Visible  = !hasProject;
		_mainContainer.Visible   = hasProject;

		if (hasProject)
			RefreshAssetList();
	}

	/// <summary>Maps tab display names to the AssetType strings used in ProjectManager.</summary>
	private static string TabNameToAssetType(string tabName) => tabName switch
	{
		"Models" => "Model",
		"Images" => "Image",
		"Audio"  => "Audio",
		"Other"  => "Other",
		_        => tabName,   // fallback: pass through as-is
	};

	private void RefreshAssetList()
	{
		_assetList.Clear();
		_displayedAssets.Clear();

		var filterType = _selectedTab == 0 ? null : TabNameToAssetType(TabNames[_selectedTab]);
		var assets     = filterType == null
			? ProjectManager.GetAssets()
			: ProjectManager.GetAssetsByType(filterType);

		foreach (var asset in assets)
		{
			var displayName = string.IsNullOrEmpty(asset.Label) ? asset.FileName : asset.Label;
			var icon        = GetIconForType(asset.AssetType);
			_assetList.AddItem($"{icon}  {displayName}");
			_displayedAssets.Add(asset);
		}

		UpdateStatusLabel();
	}

	private void UpdateStatusLabel()
	{
		if (string.IsNullOrEmpty(ProjectManager.CurrentProjectFolder))
		{
			_statusLabel.Text = "";
			return;
		}

		var dirty = ProjectManager.IsDirty ? " ●" : "";
		_statusLabel.Text =
			$"{ProjectManager.CurrentProjectName}{dirty}  |  " +
			$"{ProjectManager.GetAssets().Count} asset(s)  |  " +
			$"{ProjectManager.CurrentProjectFolder}";
	}

	private static string GetIconForType(string assetType) => assetType switch
	{
		"Model" => "🧊",
		"Image" => "🖼",
		"Audio" => "🔊",
		_       => "📄",
	};

	// ── Toolbar handlers ──────────────────────────────────────────────────────

	private void OnImportButtonPressed()
	{
		if (string.IsNullOrEmpty(ProjectManager.CurrentProjectFolder))
		{
			ShowNoProjectDialog();
			return;
		}

		var filters = new[]
		{
			"*.glb,*.gltf,*.mimodel ; 3D Models",
			"*.png,*.jpg,*.jpeg,*.bmp,*.webp ; Images",
			"*.wav,*.mp3,*.ogg ; Audio",
			"* ; All Files",
		};

		NativeFileDialog.ShowOpenFiles("Import Assets", filters, (success, paths) =>
		{
			if (!success || paths == null) return;
			foreach (var path in paths)
				ProjectManager.ImportAsset(path);
		});
	}

	private void OnOpenFolderPressed()
	{
		var folder = ProjectManager.AssetsFolder;
		if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
		{
			folder = ProjectManager.CurrentProjectFolder;
		}

		if (string.IsNullOrEmpty(folder)) return;

		// Use OS.ShellOpen to open the folder in the native file manager
		OS.ShellOpen(folder);
	}

	// ── Tab handler ───────────────────────────────────────────────────────────

	private void OnTabChanged(long tabIndex)
	{
		_selectedTab = (int)tabIndex;
		RefreshAssetList();
	}

	// ── Context menu ──────────────────────────────────────────────────────────

	/// <summary>
	/// Handles all mouse button clicks on the asset list.
	/// Left double-click spawns a Model asset; right-click opens the context menu.
	/// </summary>
	private void OnAssetItemClicked(long index, Vector2 atPosition, long mouseButtonIndex)
	{
		if (mouseButtonIndex == (long)MouseButton.Right)
		{
			_contextMenuAssetIndex = (int)index;

			// Show/hide "Spawn in Scene" based on asset type
			var asset = _displayedAssets[(int)index];
			_contextMenu.SetItemDisabled(0, asset.AssetType != "Model");

			// Show/hide "Add to Timeline" based on asset type
			// Item index 1 is "Add to Timeline" (id=4)
			_contextMenu.SetItemDisabled(1, asset.AssetType != "Audio");

			_contextMenu.Position = (Vector2I)GetGlobalMousePosition();
			_contextMenu.Popup();
		}
		else if (mouseButtonIndex == (long)MouseButton.Left)
		{
			// Double-click is signalled as two rapid ItemClicked events; Godot's
			// ItemList doesn't have a dedicated double-click signal in C#, so we
			// use ItemActivated instead (connected below).
		}
	}

	/// <summary>
	/// Called when the user double-clicks (activates) an item in the asset list.
	/// For Model assets this spawns the model into the scene.
	/// </summary>
	private void OnAssetItemActivated(long index)
	{
		if (index < 0 || index >= _displayedAssets.Count) return;
		var asset = _displayedAssets[(int)index];
		if (asset.AssetType == "Model")
			SpawnAsset(asset);
	}

	private void OnContextMenuIndexPressed(long id)
	{
		if (_contextMenuAssetIndex < 0 || _contextMenuAssetIndex >= _displayedAssets.Count)
			return;

		var asset = _displayedAssets[_contextMenuAssetIndex];

		switch (id)
		{
			case 0: // Spawn in Scene
				SpawnAsset(asset);
				break;

			case 4: // Add to Timeline
				AddAudioAssetToTimeline(asset);
				break;

			case 1: // Rename Label
				ShowRenameLabelDialog(asset);
				break;

			case 2: // Remove from Project (keep file)
				ProjectManager.RemoveAsset(asset.RelativePath, deleteFile: false);
				break;

			case 3: // Delete File
				ShowDeleteConfirmDialog(asset);
				break;
		}
	}

	// ── Spawn helper ──────────────────────────────────────────────────────────

	private void SpawnAsset(AssetEntry asset)
	{
		if (asset.AssetType != "Model")
		{
			GD.PrintErr($"ContentDrawerPanel.SpawnAsset: asset '{asset.FileName}' is not a Model");
			return;
		}

		var fullPath = ProjectManager.GetAssetFullPath(asset);
		if (!File.Exists(fullPath))
		{
			GD.PrintErr($"ContentDrawerPanel.SpawnAsset: file not found '{fullPath}'");
			return;
		}

		Main.Instance?.SpawnModelFromPath(fullPath);
	}

	// ── Audio timeline helper ─────────────────────────────────────────────────

	private void AddAudioAssetToTimeline(AssetEntry asset)
	{
		if (asset.AssetType != "Audio")
		{
			GD.PrintErr($"ContentDrawerPanel.AddAudioAssetToTimeline: asset '{asset.FileName}' is not Audio");
			return;
		}

		if (TimelinePanel.Instance != null)
		{
			TimelinePanel.Instance.AddAudioTrackFromAsset(asset);
		}
		else
		{
			// Fallback: add directly via ProjectManager
			ProjectManager.AddAudioTrack(asset.RelativePath, asset.Label ?? asset.FileName);
		}
	}

	// ── Dialogs ───────────────────────────────────────────────────────────────

	private void ShowNoProjectDialog()
	{
		var dlg = new AcceptDialog();
		dlg.Title      = "No Project Open";
		dlg.DialogText = "Please create or open a project first.\n\nUse File → New Project or File → Open Project.";
		dlg.OkButtonText = "OK";
		dlg.Exclusive  = true;
		dlg.Transient  = true;
		dlg.CloseRequested += () => { dlg.Hide(); dlg.QueueFree(); };
		AddChild(dlg);
		dlg.PopupCentered();
	}

	private void ShowRenameLabelDialog(AssetEntry asset)
	{
		var dlg = new ConfirmationDialog();
		dlg.Title      = "Rename Asset Label";
		dlg.Exclusive  = true;
		dlg.Transient  = true;

		var vbox = new VBoxContainer();
		dlg.AddChild(vbox);

		var lbl = new Label();
		lbl.Text = "Enter a display label for this asset:";
		vbox.AddChild(lbl);

		var edit = new LineEdit();
		edit.Text = string.IsNullOrEmpty(asset.Label) ? asset.FileName : asset.Label;
		edit.SelectAll();
		vbox.AddChild(edit);

		dlg.Confirmed += () =>
		{
			asset.Label = edit.Text.Trim();
			ProjectManager.MarkDirty();
			ProjectManager.NotifyAssetsChanged();   // trigger refresh
			dlg.QueueFree();
		};
		dlg.Canceled += () => dlg.QueueFree();

		AddChild(dlg);
		dlg.PopupCentered(new Vector2I(400, 120));
	}

	private void ShowDeleteConfirmDialog(AssetEntry asset)
	{
		var dlg = new ConfirmationDialog();
		dlg.Title      = "Delete Asset File";
		dlg.DialogText = $"Permanently delete '{asset.FileName}' from disk?\n\nThis cannot be undone.";
		dlg.Exclusive  = true;
		dlg.Transient  = true;

		dlg.Confirmed += () =>
		{
			ProjectManager.RemoveAsset(asset.RelativePath, deleteFile: true);
			dlg.QueueFree();
		};
		dlg.Canceled += () => dlg.QueueFree();

		AddChild(dlg);
		dlg.PopupCentered();
	}
}
