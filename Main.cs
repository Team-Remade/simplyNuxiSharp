using Gizmo3DPlugin;
using Godot;
using simplyRemadeNuxi.core;
using simplyRemadeNuxi.core.commands;
using simplyRemadeNuxi.ui;
using System;
using System.Linq;
using SceneTree = simplyRemadeNuxi.core.SceneTree;
using GDExtensionBindgen;
using FFMpegCore;
using FFMpegCore.Extensions.Downloader;
using System.Threading.Tasks;

namespace simplyRemadeNuxi;

public partial class Main : Control
{
	public static Main Instance;
	
	public RandomNumberGenerator Random = new RandomNumberGenerator();
	
	[Export] private Texture2D CheggTexture;
	
	// Performance optimization
	private int _fpsUpdateCounter = 0;
	private const int FPS_UPDATE_INTERVAL = 10; // Update FPS every 10 frames
	
	[Export] public MenuButton FileButton;
	[Export] public MenuButton EditButton;
	[Export] public MenuButton ViewButton;
	[Export] public MenuButton RenderButton;
	[Export] public MenuButton HelpButton;
	
	[Export] public TextureButton SpawnButton;
	[Export] public PreviewViewport PreviewViewportControl;
	[Export] public Button PreviewToggleButton;
	[Export] public Label MainViewportFpsLabel;
	[Export] public Label GizmoHintLabel;
	
	[Export] public SubViewport Viewport;
	[Export] public SceneTree SceneTreePanel;
	[Export] public ProjectPropertiesPanel ProjectPropertyPanel;
	[Export] public ObjectPropertiesPanel ObjectPropertyPanel;
	[Export] public ContentDrawerPanel ContentDrawerPanel;
	
	private SpawnMenu _spawnMenu;
	private bool _renderModeEnabled = false;
	
	// FFmpeg download loading UI
	private Window _ffmpegLoadingWindow;
	private Label _ffmpegLoadingLabel;
	private ProgressBar _ffmpegProgressBar;
	
	// Debug toggle: set to true to skip the asset downloader on startup
	private const bool DebugSkipAssetDownloader = false;
	
	public override void _EnterTree()
	{
		SetWindowTitle("Mine Imator Simply Remade: Nuxi");
		
		// Disable viewport rendering during asset loading to save GPU time.
		// Re-enabled in _Ready() after ShowAssetDownloaderAndWait() completes.
		DisableViewportRendering();
	}

	public override async void _Ready()
	{
		Instance = this;
		
		// ── Step 1: Download FFMpeg binaries if needed ──────────────────────────
		await EnsureFFMpegAsync();
		
		// ── Step 2: Show the AssetDownloaderWindow as a child overlay and wait ──
		if (!DebugSkipAssetDownloader)
			await ShowAssetDownloaderAndWait();
		
		// ── Step 3: Normal Main scene setup (assets are now loaded) ─────────────
		
		// Re-enable viewport rendering now that assets are loaded
		EnableViewportRendering();
		
		// Notify panels that depend on loaded assets
		ProjectPropertyPanel?.OnAssetsLoaded();
		
		SetupCommandHistory();
		SetupMenus();
		SetupSpawnMenu();
		SetupPreviewToggleButton();
		SceneTreePanel.SetViewport(Viewport);
		
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		
		SetupGizmo();
		SetupPreviewViewport();
		
		SceneTreePanel.ObjectSelected += OnSceneObjectSelected;
		SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
		ProjectManager.ProjectSaved   += OnProjectSaved;
		ProjectManager.ProjectOpened  += _ => UpdateWindowTitle();
		ProjectManager.ProjectClosed  += UpdateWindowTitle;
		ProjectManager.DirtyChanged   += _ => UpdateWindowTitle();
		
		IconEasterEgg();

		// Pre-warm the Blender PBR shader so the GPU compiles it before the first
		// GLB is loaded.  Without this, the first load triggers synchronous shader
		// compilation during rendering which can crash the application.
		BlendFileLoader.PreWarmShader();
		
		// ── Step 4: Show the project screen overlay ──────────────────────────────
		await ShowProjectScreenAndWait();
		
		// TEST: Create a BlockArrayGenerator with a few blocks
		//SetupTestBlockArrayGenerator();
	}
	
	/// <summary>
	/// Disables rendering on all SubViewports in the scene so the GPU is not
	/// busy during asset loading.
	/// </summary>
	private void DisableViewportRendering()
	{
		// Preview viewport SubViewport (instanced scene)
		var previewSubViewport = GetNodeOrNull<SubViewport>(
			"Content/MainContent/Viewport/ViewportUI/PreviewViewport/MainPanel/VBox/AspectRatioContainerNode/ViewportContainer/PreviewSubViewport");
		if (previewSubViewport != null)
			previewSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
	}
	
	/// <summary>
	/// Re-enables rendering on all SubViewports after asset loading is complete.
	/// </summary>
	private void EnableViewportRendering()
	{
		// Preview viewport SubViewport – restore to ALWAYS so it is ready when toggled on
		if (PreviewViewportControl?.PreviewSubViewport != null)
		{
			PreviewViewportControl.PreviewSubViewport.RenderTargetUpdateMode =
				SubViewport.UpdateMode.Always;
		}
		else
		{
			// Fallback: find by path if the export ref isn't wired yet
			var previewSubViewport = GetNodeOrNull<SubViewport>(
				"Content/MainContent/Viewport/ViewportUI/PreviewViewport/MainPanel/VBox/AspectRatioContainerNode/ViewportContainer/PreviewSubViewport");
			if (previewSubViewport != null)
				previewSubViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		}
	}
	
	/// <summary>
	/// Ensures FFMpeg binaries are present, downloading them if necessary.
	/// Mirrors the logic that was previously in Launcher.cs.
	/// </summary>
	private async System.Threading.Tasks.Task EnsureFFMpegAsync()
	{
		try
		{
			var ffmpegPath = System.IO.Path.Combine(OS.GetUserDataDir(), "ffmpeg");
			System.IO.Directory.CreateDirectory(ffmpegPath);
			GlobalFFOptions.Configure(options => options.BinaryFolder = ffmpegPath);
			
			bool ffmpegAvailable = false;
			try
			{
				var process = new System.Diagnostics.Process();
				process.StartInfo.FileName = GlobalFFOptions.GetFFMpegBinaryPath();
				process.StartInfo.Arguments = "-version";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				process.WaitForExit();
				ffmpegAvailable = process.ExitCode == 0;
			}
			catch
			{
				ffmpegAvailable = false;
			}
			
			if (!ffmpegAvailable)
			{
				// Show loading UI while downloading FFmpeg
				ShowFFmpegLoadingWindow("Downloading FFmpeg binaries...");
				GD.Print("Downloading FFMpeg binaries...");
				await FFMpegDownloader.DownloadBinaries();
				CloseFFmpegLoadingWindow();
			}
		}
		catch (System.Exception ex)
		{
			CloseFFmpegLoadingWindow();
			GD.PrintErr($"Failed to ensure FFMpeg: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Shows a loading window with the given message during FFmpeg download.
	/// </summary>
	private void ShowFFmpegLoadingWindow(string message)
	{
		_ffmpegLoadingWindow = new Window();
		_ffmpegLoadingWindow.Title = "Loading";
		_ffmpegLoadingWindow.Size = new Vector2I(400, 150);
		_ffmpegLoadingWindow.Unresizable = true;
		_ffmpegLoadingWindow.Borderless = false;
		_ffmpegLoadingWindow.AlwaysOnTop = true;
		
		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		vbox.AddThemeConstantOverride("separation", 20);
		
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		
		_ffmpegLoadingLabel = new Label();
		_ffmpegLoadingLabel.Text = message;
		_ffmpegLoadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_ffmpegLoadingLabel.VerticalAlignment = VerticalAlignment.Center;
		
		_ffmpegProgressBar = new ProgressBar();
		_ffmpegProgressBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_ffmpegProgressBar.Indeterminate = true;
		
		vbox.AddChild(_ffmpegLoadingLabel);
		vbox.AddChild(_ffmpegProgressBar);
		margin.AddChild(vbox);
		_ffmpegLoadingWindow.AddChild(margin);
		
		AddChild(_ffmpegLoadingWindow);
		_ffmpegLoadingWindow.Position = (DisplayServer.ScreenGetSize() / 2) - (_ffmpegLoadingWindow.Size / 2);
		_ffmpegLoadingWindow.Show();
	}
	
	/// <summary>
	/// Closes the FFmpeg loading window.
	/// </summary>
	private void CloseFFmpegLoadingWindow()
	{
		if (_ffmpegLoadingWindow != null)
		{
			_ffmpegLoadingWindow.QueueFree();
			_ffmpegLoadingWindow = null;
			_ffmpegLoadingLabel = null;
			_ffmpegProgressBar = null;
		}
	}
	
	/// <summary>
	/// Instantiates the AssetDownloaderWindow as a child of this node, waits for
	/// it to emit <c>LoadingComplete</c>, then returns so Main can finish setup.
	/// </summary>
	private async System.Threading.Tasks.Task ShowAssetDownloaderAndWait()
	{
		var assetDownloaderScene = GD.Load<PackedScene>("res://AssetDownloaderWindow.tscn");
		if (assetDownloaderScene == null)
		{
			GD.PrintErr("Could not load AssetDownloaderWindow.tscn");
			return;
		}
		
		var assetDownloaderWindow = assetDownloaderScene.Instantiate<AssetDownloaderWindow>();
		AddChild(assetDownloaderWindow);
		assetDownloaderWindow.Show();
		
		// Wait until the downloader signals that all assets are ready
		await ToSignal(assetDownloaderWindow, AssetDownloaderWindow.SignalName.LoadingComplete);
	}
	
	/// <summary>
	/// Instantiates the ProjectScreen as a child overlay, waits for the user to
	/// choose or create a project (ProjectChosen signal), then removes the overlay.
	/// </summary>
	private async System.Threading.Tasks.Task ShowProjectScreenAndWait()
	{
		var projectScreenScene = GD.Load<PackedScene>("res://scenes/ProjectScreen.tscn");
		if (projectScreenScene == null)
		{
			GD.PrintErr("Could not load ProjectScreen.tscn – skipping project screen");
			return;
		}

		var projectScreen = projectScreenScene.Instantiate<ProjectScreen>();
		AddChild(projectScreen);
		projectScreen.Show();

		// Wait until the user picks a project
		await ToSignal(projectScreen, ProjectScreen.SignalName.ProjectChosen);

		projectScreen.Hide();
		projectScreen.QueueFree();
	}

	/// <summary>
	/// TEMPORARY TEST: Creates a BlockArrayGenerator with a few blocks to verify the voxel pipeline.
	/// Remove when testing is complete.
	/// </summary>
	private void SetupTestBlockArrayGenerator()
	{
		if (VoxelSettings.Instance?.Mesher == null)
		{
			GD.PrintErr("SetupTestBlockArrayGenerator: VoxelSettings not ready");
			return;
		}
		
		var gen = new BlockArrayGenerator();
		gen.Name = "TestBlockArrayGenerator";
		Viewport.AddChild(gen);
		
		gen.SetBlocks(new[]
		{
			new BlockPlacement { Position = new Vector3I(0, 0, 0), BlockName = "stone" },
			new BlockPlacement { Position = new Vector3I(1, 0, 0), BlockName = "grass_block" },
			new BlockPlacement { Position = new Vector3I(2, 0, 0), BlockName = "dirt" },
			new BlockPlacement { Position = new Vector3I(3, 0, 0), BlockName = "oak_planks" },
			new BlockPlacement { Position = new Vector3I(4, 0, 0), BlockName = "cobblestone" },
		});
		
		gen.Initialize(
			bounds: new Aabb(new Vector3(-16, -16, -16), new Vector3(32, 32, 32)),
			viewDistance: 64
		);
	}

	private void IconEasterEgg()
	{
		var randInt = Random.RandiRange(1, 1000);
		if (randInt == 777)
		{
			SetWindowIcon(CheggTexture.GetImage());
		}
	}

	private void SetupGizmo()
	{
		var gizmo = new Gizmo3D();
		gizmo.Name = "Gizmo";
		Viewport.AddChild(gizmo);
		gizmo.ShowSelectionBox = false;
		gizmo.Layers = 2; // Set to layer 2 so it can be hidden from certain cameras
		SelectionManager.Instance.Gizmo = gizmo;
		
		// Connect gizmo signals for auto-keyframing after gizmo is initialized
		SelectionManager.Instance.ConnectGizmoSignals();
	}

	private void SetupPreviewViewport()
	{
		if (PreviewViewportControl != null)
		{
			// Set the main viewport for shared World3D
			PreviewViewportControl.SetMainViewport(Viewport);
			
			// Get the main camera
			var mainCamera = Viewport.GetNode<Camera3D>("WorkCam");
			if (mainCamera != null && PreviewViewportControl.PreviewCamera != null)
			{
				// Sync the preview camera with the main camera
				PreviewViewportControl.SyncWithMainCamera(mainCamera);
			}
		}
	}

	private void SetupCommandHistory()
	{
		var history = new EditorCommandHistory();
		history.Name = "EditorCommandHistory";
		AddChild(history);

		// Connect to history changes to update the Edit menu
		history.HistoryChanged += OnHistoryChanged;

		// Initialize menu state (disabled until there's something to undo)
		OnHistoryChanged();

		// Clear history when a new project is opened or created
		ProjectManager.ProjectOpened  += _ => EditorCommandHistory.Instance?.Clear();
		ProjectManager.ProjectClosed  += () => EditorCommandHistory.Instance?.Clear();
	}

	/// <summary>
	/// Updates the Edit menu items to show the current undo/redo action and
	/// enables/disables them based on whether there's history to undo/redo.
	/// </summary>
	private void OnHistoryChanged()
	{
		var editPopup = EditButton?.GetPopup();
		if (editPopup == null) return;

		var history = EditorCommandHistory.Instance;
		if (history == null) return;

		// Update Undo menu item (id 0)
		if (history.CanUndo)
		{
			editPopup.SetItemText(0, $"Undo: {history.UndoDescription}\tCtrl+Z");
			editPopup.SetItemDisabled(0, false);
		}
		else
		{
			editPopup.SetItemText(0, "Undo\tCtrl+Z");
			editPopup.SetItemDisabled(0, true);
		}

		// Update Redo menu item (id 1)
		if (history.CanRedo)
		{
			editPopup.SetItemText(1, $"Redo: {history.RedoDescription}\tCtrl+Y");
			editPopup.SetItemDisabled(1, false);
		}
		else
		{
			editPopup.SetItemText(1, "Redo\tCtrl+Y");
			editPopup.SetItemDisabled(1, true);
		}
	}

	private void SetupMenus()
	{
		//Setup File Menu
		var filePopup = FileButton.GetPopup();
		filePopup.AddItem("New Project",  0);
		filePopup.AddItem("Open Project", 1);
		filePopup.AddItem("Save Project", 2);
		filePopup.AddSeparator();
		filePopup.AddItem("Exit", 3);
		filePopup.IndexPressed += OnFileMenuPressed;
		
		//Setup Edit Menu
		var editPopup = EditButton.GetPopup();
		editPopup.AddItem("Undo\tCtrl+Z", 0);
		editPopup.AddItem("Redo\tCtrl+Y", 1);
		editPopup.AddSeparator();
		editPopup.AddItem("Cut", 2);
		editPopup.AddItem("Copy", 3);
		editPopup.AddItem("Paste", 4);
		editPopup.AddItem("Delete", 5);
		editPopup.IndexPressed += OnEditMenuPressed;
		
		//Setup View Menu
		var viewPopup = ViewButton.GetPopup();
		viewPopup.AddItem("Show Grid", 0);
		viewPopup.AddItem("Show Gizmos", 1);
		viewPopup.AddItem("Show Overlays", 2);
		viewPopup.AddSeparator();
		viewPopup.AddItem("Top View", 3);
		viewPopup.AddItem("Side View", 4);
		viewPopup.AddItem("Front View", 5);
		viewPopup.AddItem("Orthographic View", 6);
		//connect
		
		//Setup Render Menu
		var renderPopup = RenderButton.GetPopup();
		renderPopup.AddItem("Render Image", 0);
		renderPopup.AddItem("Render Animation", 1);
		renderPopup.AddSeparator();
		renderPopup.AddItem("Render Settings", 2);
		renderPopup.IndexPressed += OnRenderMenuPressed;
		
		//Setup Help Menu
		var helpPopup = HelpButton.GetPopup();
		helpPopup.AddItem("Help", 0);
		helpPopup.AddItem("Tutorials", 1);
		//connect
	}

	private void SetupSpawnMenu()
	{
		_spawnMenu = new SpawnMenu();
		_spawnMenu.Viewport = Viewport;
		AddChild(_spawnMenu);
		_spawnMenu.Hide();
		
		// Connect the SpawnButton pressed signal to show the menu
		SpawnButton.Pressed += OnSpawnButtonPressed;
	}

	/// <summary>
	/// Spawns a 3D model from an absolute file path into the current scene.
	/// Called by the Content Drawer when the user double-clicks a Model asset.
	/// </summary>
	public void SpawnModelFromPath(string modelPath)
	{
		_spawnMenu?.SpawnModelFromPath(modelPath);
	}

	/// <summary>
	/// Awaitable version used by the project restore system.
	/// </summary>
	public System.Threading.Tasks.Task SpawnModelFromPathAsync(string modelPath)
	{
		if (_spawnMenu == null) return System.Threading.Tasks.Task.CompletedTask;
		return _spawnMenu.SpawnModelFromPathAsync(modelPath);
	}

	/// <summary>
	/// Creates a primitive SceneObject and adds it to the viewport.
	/// Used by the project restore system.
	/// </summary>
	public SceneObject SpawnPrimitiveObject(string primitiveType, string objectName)
	{
		return _spawnMenu?.SpawnPrimitiveObject(primitiveType, objectName);
	}

	/// <summary>Creates a LightSceneObject and adds it to the viewport.</summary>
	public LightSceneObject SpawnLightObject(string objectName)
	{
		return _spawnMenu?.SpawnLightObject(objectName);
	}

	/// <summary>Creates a CameraSceneObject, adds it to the viewport, and notifies the preview viewport.</summary>
	public CameraSceneObject SpawnCameraObject(string objectName)
	{
		var cameraObject = _spawnMenu?.SpawnCameraObject(objectName);
		if (cameraObject != null)
		{
			PreviewViewportControl?.OnCameraSpawned(cameraObject);
			SceneTreePanel?.Refresh();
		}
		return cameraObject;
	}

	/// <summary>Creates a Minecraft block SceneObject and adds it to the viewport.</summary>
	public SceneObject SpawnBlockObject(string blockName, string variant, string objectName)
	{
		return _spawnMenu?.SpawnBlockObject(blockName, variant, objectName);
	}

	/// <summary>Creates a Minecraft item/block texture plane SceneObject and adds it to the viewport.</summary>
	public SceneObject SpawnItemObject(string itemName, string textureType, string objectName)
	{
		return _spawnMenu?.SpawnItemObject(itemName, textureType, objectName);
	}
	
	private void SetupPreviewToggleButton()
	{
		if (PreviewToggleButton != null)
		{
			PreviewToggleButton.Pressed += OnPreviewToggleButtonPressed;
		}
		
		// Set initial visibility to false for both preview and render mode
		if (PreviewViewportControl != null)
		{
			PreviewViewportControl.Visible = false;
		}
	}
	
	private void OnPreviewToggleButtonPressed()
	{
		TogglePreviewViewportVisibility();
	}
	
	private void OnSpawnButtonPressed()
	{
		// Show the spawn menu at the global position of the spawn button
		var menuPosition = SpawnButton.GlobalPosition;
		_spawnMenu.ShowMenu(menuPosition);
	}

	private void OnFileMenuPressed(long id)
	{
		switch (id)
		{
			case 0: // New Project
				ShowNewProjectDialog();
				break;
			case 1: // Open Project
				ShowOpenProjectDialog();
				break;
			case 2: // Save Project
				OnSaveProject();
				break;
			case 3: // Exit
				GetTree().Quit();
				break;
		}
	}

	private void OnEditMenuPressed(long id)
	{
		switch (id)
		{
			case 0: // Undo
				EditorCommandHistory.Instance?.Undo();
				break;
			case 1: // Redo
				EditorCommandHistory.Instance?.Redo();
				break;
		}
	}

	// ── Project file dialogs ──────────────────────────────────────────────────

	private void ShowNewProjectDialog()
	{
		var lastFolder = ProjectManager.GetLastNewProjectFolder();
		NativeFileDialog.ShowOpenDirectory("Choose New Project Folder", (success, folderPath) =>
		{
			if (!success || string.IsNullOrEmpty(folderPath)) return;

			// Persist the chosen folder for next time
			ProjectManager.SaveLastNewProjectFolder(folderPath);

			// Ask for a project name
			var dlg = new ConfirmationDialog();
			dlg.Title     = "New Project";
			dlg.Exclusive = true;
			dlg.Transient = true;

			var vbox = new VBoxContainer();
			dlg.AddChild(vbox);

			var lbl = new Label();
			lbl.Text = "Enter a name for the new project:";
			vbox.AddChild(lbl);

			var edit = new LineEdit();
			edit.Text = "MyProject";
			edit.SelectAll();
			vbox.AddChild(edit);

			dlg.Confirmed += () =>
			{
				var projectName = edit.Text.Trim();
				if (string.IsNullOrEmpty(projectName)) projectName = "MyProject";

				var projectFolder = System.IO.Path.Combine(folderPath, projectName);
				if (ProjectManager.NewProject(projectFolder))
					{
						UpdateWindowTitle();
					}
					dlg.QueueFree();
			};
			dlg.Canceled += () => dlg.QueueFree();

			AddChild(dlg);
			dlg.PopupCentered(new Vector2I(400, 120));
		}, startDirectory: lastFolder);
	}

	private void ShowOpenProjectDialog()
	{
		NativeFileDialog.ShowOpenFile(
			"Open Project",
			new[] { "*.srproject ; Simply Remade Project" },
			(success, filePath) =>
			{
				if (!success || string.IsNullOrEmpty(filePath)) return;

				if (ProjectManager.OpenProject(filePath))
					{
						UpdateWindowTitle();
						// Restore scene objects with a progress window
						_ = RestoreSceneWithProgressAsync();
					}
			});
	}

	// ── Project screen variants (called from ProjectScreen overlay) ───────────

	/// <summary>
	/// Shows the new-project dialog and invokes <paramref name="onSuccess"/> if a project
	/// is successfully created.  Used by the startup project screen.
	/// </summary>
	public void ShowNewProjectDialogFromScreen(Action onSuccess)
	{
		var lastFolder = ProjectManager.GetLastNewProjectFolder();
		NativeFileDialog.ShowOpenDirectory("Choose New Project Folder", (success, folderPath) =>
		{
			if (!success || string.IsNullOrEmpty(folderPath)) return;

			// Persist the chosen folder for next time
			ProjectManager.SaveLastNewProjectFolder(folderPath);

			var dlg = new ConfirmationDialog();
			dlg.Title     = "New Project";
			dlg.Exclusive = true;
			dlg.Transient = true;

			var vbox = new VBoxContainer();
			dlg.AddChild(vbox);

			var lbl = new Label();
			lbl.Text = "Enter a name for the new project:";
			vbox.AddChild(lbl);

			var edit = new LineEdit();
			edit.Text = "MyProject";
			edit.SelectAll();
			vbox.AddChild(edit);

			dlg.Confirmed += () =>
			{
				var projectName = edit.Text.Trim();
				if (string.IsNullOrEmpty(projectName)) projectName = "MyProject";

				var projectFolder = System.IO.Path.Combine(folderPath, projectName);
				if (ProjectManager.NewProject(projectFolder))
					{
						UpdateWindowTitle();
						onSuccess?.Invoke();
					}
				dlg.QueueFree();
			};
			dlg.Canceled += () => dlg.QueueFree();

			AddChild(dlg);
			dlg.PopupCentered(new Vector2I(400, 120));
		}, startDirectory: lastFolder);
	}

	/// <summary>
	/// Shows the open-project dialog and invokes <paramref name="onSuccess"/> if a project
	/// is successfully opened.  Used by the startup project screen.
	/// </summary>
	public void ShowOpenProjectDialogFromScreen(Action onSuccess)
	{
		NativeFileDialog.ShowOpenFile(
			"Open Project",
			new[] { "*.srproject ; Simply Remade Project" },
			(success, filePath) =>
			{
				if (!success || string.IsNullOrEmpty(filePath)) return;

				if (ProjectManager.OpenProject(filePath))
					{
						UpdateWindowTitle();
						_ = RestoreSceneWithProgressAsync();
						onSuccess?.Invoke();
					}
			});
	}

	public async System.Threading.Tasks.Task RestoreSceneWithProgressAsync()
	{
		// Clear any active selection before wiping the scene so the gizmo and
		// property panels don't hold stale references to objects about to be freed.
		SelectionManager.Instance?.ClearSelection();

		// Refresh the scene tree panel immediately so it shows an empty tree
		// while the new project is being loaded.
		SceneTreePanel?.Refresh();

		// Count how many models need to be loaded
		var assets = ProjectManager.GetAssets();

		// Create and show the progress window
		var progressWindow = new ModelLoadingProgressWindow();
		progressWindow.Title = "Loading Project";
		GetTree().Root.AddChild(progressWindow);
		progressWindow.SetModelName(ProjectManager.CurrentProjectName);
		progressWindow.ShowWindow();
		progressWindow.UpdateProgress(0f, "Preparing...");

		await ProjectManager.RestoreSceneStateAsync((loaded, total, modelName) =>
		{
			if (total == 0) return;
			float progress = (float)loaded / total;
			var msg = loaded < total
				? $"Loading model {loaded + 1} of {total}: {modelName}"
				: "Done";
			progressWindow.UpdateProgress(progress, msg);
		});

		progressWindow.HideWindow();
		progressWindow.QueueFree();

		// Refresh the scene tree panel
		SceneTreePanel?.Refresh();

		// Load keyframes from all restored SceneObjects into the timeline's working
		// dictionary, then seek to the saved frame so positions are applied correctly.
		if (TimelinePanel.Instance != null && Viewport != null)
		{
			var allObjects = new System.Collections.Generic.List<SceneObject>();
			foreach (var child in Viewport.GetChildren())
			{
				if (child is SceneObject so)
				{
					allObjects.Add(so);
					// Also include all descendants (e.g. bones/parts of a model)
					allObjects.AddRange(so.GetAllDescendants());
				}
			}

			TimelinePanel.Instance.LoadKeyframesForAllObjects(allObjects);

			// Seek to the frame that was active when the project was saved.
			// This re-applies keyframe values for animated objects so they appear
			// at the correct position, and leaves non-animated objects untouched
			// (their CurrentFramePosition was already applied during restore).
			int savedFrame = ProjectManager.GetSavedCurrentFrame();
			TimelinePanel.Instance.SetCurrentFrame(savedFrame);
		}
	}

	private void OnProjectSaved()
	{
		ToastNotification.Show(this, "Project saved");
		UpdateWindowTitle();
	}

	private void OnSaveProject()
	{
		if (string.IsNullOrEmpty(ProjectManager.CurrentProjectFile))
		{
			// No project open — prompt to create one first
			var dlg = new AcceptDialog();
			dlg.Title      = "No Project Open";
			dlg.DialogText = "Please create or open a project before saving.\n\nUse File → New Project or File → Open Project.";
			dlg.OkButtonText = "OK";
			dlg.Exclusive  = true;
			dlg.Transient  = true;
			dlg.CloseRequested += () => { dlg.Hide(); dlg.QueueFree(); };
			AddChild(dlg);
			dlg.PopupCentered();
			return;
		}

		ProjectManager.SaveProject();
		// Title is updated via the ProjectSaved → OnProjectSaved → UpdateWindowTitle chain
	}
	
	private void OnRenderMenuPressed(long id)
	{
		switch (id)
		{
			case 0: // Render Image
				ShowRenderImageDialog();
				break;
			case 1: // Render Animation
				ShowRenderAnimationDialog();
				break;
			case 2: // Render Settings
				// TODO: Implement render settings dialog
				break;
		}
	}
	
	private void ShowRenderImageDialog()
	{
		// Ensure render mode is enabled
		if (!_renderModeEnabled)
		{
			ToggleRenderMode();
		}
		
		// Get render resolution from project properties
		int width = ProjectPropertyPanel.GetRenderWidth();
		int height = ProjectPropertyPanel.GetRenderHeight();
		
		// Create and show dialog
		var dialog = new RenderImageDialog();
		dialog.SetResolution(width, height);
		dialog.SetRenderCallback((filePath, format) =>
		{
			PreviewViewportControl.RenderImage(filePath, format, width, height);
		});
		
		AddChild(dialog);
	}
	
	private void ShowRenderAnimationDialog()
	{
		// Ensure render mode is enabled
		if (!_renderModeEnabled)
		{
			ToggleRenderMode();
		}
		
		// Get render settings
		int width = ProjectPropertyPanel.GetRenderWidth();
		int height = ProjectPropertyPanel.GetRenderHeight();
		float framerate = ProjectPropertyPanel.GetFramerate();
		int lastKeyframe = TimelinePanel.Instance?.GetLastKeyframe() ?? 0;
		
		if (lastKeyframe == 0)
		{
			GD.PrintErr("Cannot render animation: No keyframes found");
			
			// Show error dialog to user
			var errorDialog = new AcceptDialog();
			errorDialog.Title = "Cannot Render Animation";
			errorDialog.DialogText = "Cannot render animation: No keyframes found in the timeline.\n\nPlease add keyframes to your animation before rendering.";
			errorDialog.OkButtonText = "OK";
			errorDialog.Exclusive = true;
			errorDialog.Transient = true;
			errorDialog.CloseRequested += () =>
			{
				errorDialog.Hide();
				errorDialog.QueueFree();
			};
			AddChild(errorDialog);
			errorDialog.PopupCentered();
			return;
		}
		
		// Create and show dialog
		var dialog = new RenderAnimationDialog();
		dialog.SetResolution(width, height);
		dialog.SetFramerate(framerate);
		dialog.SetLastKeyframe(lastKeyframe);
		dialog.SetRenderCallback((outputPath, format, isPngSequence, bitrateMbps) =>
		{
			PreviewViewportControl.RenderAnimation(outputPath, format, isPngSequence, bitrateMbps, width, height, framerate, lastKeyframe);
		});
		
		AddChild(dialog);
	}
	
	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			if (ProjectManager.IsDirty)
			{
				// Intercept the close and show a save-confirmation dialog
				ShowUnsavedChangesDialog(onQuit: () =>
				{
					NativeFileDialog.CloseAllDialogs();
					GetTree().Quit();
				});
				return;
			}

			// No unsaved changes – close immediately
			NativeFileDialog.CloseAllDialogs();
			GetTree().Quit();
		}
	}

	/// <summary>
	/// Shows a modal dialog asking the user what to do about unsaved changes.
	/// <paramref name="onQuit"/> is called when the application should actually quit.
	/// </summary>
	private void ShowUnsavedChangesDialog(Action onQuit)
	{
		var dlg = new ConfirmationDialog();
		dlg.Title           = "Unsaved Changes";
		dlg.DialogText      = $"The project \"{ProjectManager.CurrentProjectName}\" has unsaved changes.\n\nDo you want to save before closing?";
		dlg.OkButtonText    = "Save";
		dlg.CancelButtonText = "Close Without Saving";
		dlg.Exclusive       = true;
		dlg.Transient       = true;

		// "Save" button
		dlg.Confirmed += () =>
		{
			dlg.QueueFree();
			ProjectManager.SaveProject();
			onQuit?.Invoke();
		};

		// "Close Without Saving" button (the Cancel button)
		dlg.Canceled += () =>
		{
			dlg.QueueFree();
			onQuit?.Invoke();
		};

		// Add a real Cancel button so the user can abort the close entirely.
		// Godot's ConfirmationDialog only has OK + Cancel, so we repurpose the
		// built-in Cancel as "Close Without Saving" and add a third button for
		// "Cancel" (abort the quit).
		dlg.AddButton("Cancel", true, "cancel");
		dlg.CustomAction += (action) =>
		{
			if (action == "cancel")
			{
				dlg.QueueFree();
				// Do nothing – the user chose to stay in the app
			}
		};

		AddChild(dlg);
		dlg.PopupCentered(new Vector2I(460, 140));
	}

	/// <summary>
	/// Updates the OS window title to reflect the current project name and dirty state.
	/// Format: "Mine Imator Simply Remade: Nuxi — ProjectName*"  (asterisk when dirty)
	/// </summary>
	public void UpdateWindowTitle()
	{
		const string AppName = "Mine Imator Simply Remade: Nuxi";

		if (string.IsNullOrEmpty(ProjectManager.CurrentProjectName) ||
		    ProjectManager.CurrentProjectName == "Untitled" &&
		    string.IsNullOrEmpty(ProjectManager.CurrentProjectFile))
		{
			SetWindowTitle(AppName);
			return;
		}

		var dirty = ProjectManager.IsDirty ? "*" : "";
		SetWindowTitle($"{AppName} — {ProjectManager.CurrentProjectName}{dirty}");
	}

	public void SetWindowTitle(string title)
	{
		var window = GetWindow();
		window.Title = title;
	}

	public void SetWindowIcon(Image icon)
	{
		DisplayServer.SetIcon(icon);
	}

	private void OnSceneObjectSelected(SceneObject sceneObject)
	{
		SelectionManager.Instance.ClearSelection();
		SelectionManager.Instance.SelectObject(sceneObject);
	}
	
	private void OnSelectionChanged()
	{
		// Show the gizmo hint label only when objects are selected
		if (GizmoHintLabel != null)
		{
			GizmoHintLabel.Visible = SelectionManager.Instance.SelectedObjects.Count > 0;
		}
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			// F5 to toggle render mode
			if (keyEvent.Keycode == Key.F5)
			{
				ToggleRenderMode();
				GetViewport().SetInputAsHandled();
			}
			
			// Ctrl+S to save the project
			if (keyEvent.Keycode == Key.S && keyEvent.CtrlPressed)
			{
				OnSaveProject();
				GetViewport().SetInputAsHandled();
			}

			// Ctrl+D to duplicate selected objects
			if (keyEvent.Keycode == Key.D && keyEvent.CtrlPressed)
			{
				if (SelectionManager.Instance.SelectedObjects.Count > 0)
				{
					SceneTreePanel.DuplicateSelectedObjects();
					GetViewport().SetInputAsHandled();
				}
			}

			// Ctrl+Z to undo
			if (keyEvent.Keycode == Key.Z && keyEvent.CtrlPressed && !keyEvent.ShiftPressed)
			{
				EditorCommandHistory.Instance?.Undo();
				GetViewport().SetInputAsHandled();
			}

			// Ctrl+Y or Ctrl+Shift+Z to redo
			if ((keyEvent.Keycode == Key.Y && keyEvent.CtrlPressed) ||
			    (keyEvent.Keycode == Key.Z && keyEvent.CtrlPressed && keyEvent.ShiftPressed))
			{
				EditorCommandHistory.Instance?.Redo();
				GetViewport().SetInputAsHandled();
			}
		}
	}
	
	public bool IsRenderModeEnabled()
	{
		return _renderModeEnabled;
	}
	
	private async Task ToggleRenderMode()
	{
		_renderModeEnabled = !_renderModeEnabled;
		
		// Check if preview is visible
		bool previewVisible = PreviewViewportControl != null && PreviewViewportControl.Visible;
		
		// Update all lights in the scene
		UpdateLightsRenderMode(Viewport, _renderModeEnabled);
		
		// Update FPS labels visibility based on which viewport is rendering
		if (_renderModeEnabled && previewVisible)
		{
			// Preview is visible, show FPS on preview
			if (MainViewportFpsLabel != null)
				MainViewportFpsLabel.Visible = false;
			if (PreviewViewportControl != null)
				PreviewViewportControl.SetFpsLabelVisible(true);
		}
		else if (_renderModeEnabled && !previewVisible)
		{
			// Preview is hidden, show FPS on main viewport
			if (MainViewportFpsLabel != null)
				MainViewportFpsLabel.Visible = true;
			if (PreviewViewportControl != null)
				PreviewViewportControl.SetFpsLabelVisible(false);
		}
		else
		{
			// Render mode disabled, hide all FPS labels
			if (MainViewportFpsLabel != null)
				MainViewportFpsLabel.Visible = false;
			if (PreviewViewportControl != null)
				PreviewViewportControl.SetFpsLabelVisible(false);
		}
		
		// If preview is visible and render mode is enabled, show the preview
		if (_renderModeEnabled && previewVisible && PreviewViewportControl != null)
		{
			PreviewViewportControl.Visible = true;
		}

		await ToggleTAA(Viewport);
		await ToggleTAA(PreviewViewportControl.PreviewSubViewport);
	}

	private async Task ToggleTAA(SubViewport viewport)
	{
		viewport.UseTaa = true;

		// Wait for a few frames
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);

		viewport.UseTaa = false;
	}
	
	private void UpdateLightsRenderMode(Node node, bool enabled)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is LightSceneObject lightObject)
			{
				lightObject.SetRenderMode(enabled);
			}
			
			// Recursively update child nodes
			if (child is Node childNode && childNode.GetChildCount() > 0)
			{
				UpdateLightsRenderMode(childNode, enabled);
			}
		}
		
		// Update Sun and Moon shadow settings based on render mode
		UpdateSunMoonShadows(node, enabled);
	}
	
	/// <summary>
	/// Temporarily enables render mode for a few frames then disables it again.
	/// Used when enabling Advanced Sky to initialize cloud shadows properly.
	/// </summary>
	public async void TemporarilyEnableRenderMode()
	{
		// Store original state
		bool originalState = _renderModeEnabled;
		
		// Enable render mode temporarily
		if (!_renderModeEnabled)
		{
			ToggleRenderMode();
		}
		
		// Wait for a few frames
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		
		// Disable render mode if it was originally disabled
		if (!originalState)
		{
			ToggleRenderMode();
		}
	}
	
	/// <summary>
	/// Updates the shadow settings on the Sun and Moon directional lights.
	/// When in rendered mode, shadows are enabled. When in unrendered mode, shadows are disabled.
	/// </summary>
	private void UpdateSunMoonShadows(Node node, bool enabled)
	{
		// Find Sun directional light
		var sun = node.GetNodeOrNull<DirectionalLight3D>("Sun");
		if (sun != null)
		{
			sun.ShadowEnabled = enabled;
			
			// Find Moon directional light (child of Sun)
			var moon = sun.GetNodeOrNull<DirectionalLight3D>("Moon");
			if (moon != null)
			{
				moon.ShadowEnabled = enabled;
			}
		}
	}
	
	public void TogglePreviewViewportVisibility()
	{
		if (PreviewViewportControl == null)
			return;
		
		bool wasVisible = PreviewViewportControl.Visible;
		PreviewViewportControl.Visible = !wasVisible;
		
		
		// Update FPS label visibility if render mode is on
		if (_renderModeEnabled)
		{
			if (PreviewViewportControl.Visible)
			{
				// Preview now visible, show FPS on preview
				if (MainViewportFpsLabel != null)
					MainViewportFpsLabel.Visible = false;
				PreviewViewportControl.SetFpsLabelVisible(true);
			}
			else
			{
				// Preview now hidden, show FPS on main viewport
				if (MainViewportFpsLabel != null)
					MainViewportFpsLabel.Visible = true;
				PreviewViewportControl.SetFpsLabelVisible(false);
			}
		}
	}
	
	public override void _Process(double delta)
	{
		base._Process(delta);
		
		// Update main viewport FPS counter less frequently to reduce overhead
		if (MainViewportFpsLabel != null && MainViewportFpsLabel.Visible)
		{
			_fpsUpdateCounter++;
			if (_fpsUpdateCounter >= FPS_UPDATE_INTERVAL)
			{
				_fpsUpdateCounter = 0;
				int fps = (int)Engine.GetFramesPerSecond();
				MainViewportFpsLabel.Text = $"FPS: {fps}";
			}
		}
		
		// Draw debug range spheres for all selected lights
		DrawSelectedLightRanges();
	}
	
	/// <summary>
	/// Uses DebugDraw3D to draw a wireframe sphere showing the range of each selected light.
	/// The sphere is drawn every frame (duration = 0) so it disappears when the light is deselected.
	/// </summary>
	private void DrawSelectedLightRanges()
	{
		// Never draw debug overlays during image/animation rendering
		if (_renderModeEnabled)
			return;
		
		if (SelectionManager.Instance == null || SelectionManager.Instance.SelectedObjects.Count == 0)
			return;
		
		foreach (var obj in SelectionManager.Instance.SelectedObjects)
		{
			if (obj is LightSceneObject lightObj)
			{
				// Use a scoped config targeting the main SubViewport so the sphere
				// appears in the correct 3D world and not the editor root viewport.
				using var scopedCfg = DebugDraw3D.NewScopedConfig()?.SetViewport(Viewport);
				
				var position = lightObj.GlobalPosition;
				var range = lightObj.LightRange;
				// Use the light's color with full alpha for the range indicator
				var color = new Color(lightObj.LightColor, 1.0f);
				
				DebugDraw3D.DrawSphere(position, range, color);
			}
		}
	}
}
