using Gizmo3DPlugin;
using Godot;
using simplyRemadeNuxi.core;
using simplyRemadeNuxi.ui;
using System.Linq;
using SceneTree = simplyRemadeNuxi.core.SceneTree;

namespace simplyRemadeNuxi;

public partial class Main : Control
{
	public static Main Instance;
	
	public RandomNumberGenerator Random = new RandomNumberGenerator();
	
	[Export] private Texture2D CheggTexture;
	
	[Export] public MenuButton FileButton;
	[Export] public MenuButton EditButton;
	[Export] public MenuButton ViewButton;
	[Export] public MenuButton RenderButton;
	[Export] public MenuButton HelpButton;
	
	[Export] public TextureButton SpawnButton;
	[Export] public PreviewViewport PreviewViewportControl;
		
	[Export] public SubViewport Viewport;
	[Export] public SceneTree SceneTreePanel;
	[Export] public Control SceneTree;
	[Export] public ProjectPropertiesPanel ProjectPropertyPanel;
	[Export] public ObjectPropertiesPanel ObjectPropertyPanel;
	
	private SpawnMenu _spawnMenu;
	
	public override void _EnterTree()
	{
		SetWindowTitle("Mine Imator Simply Remade: Nuxi");
	}

	public override async void _Ready()
	{
		Instance = this;
		
		SetupMenus();
		SetupSpawnMenu();
		SceneTreePanel.SetViewport(Viewport);
		
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		
		SetupGizmo();
		SetupPreviewViewport();
		
		SceneTreePanel.ObjectSelected += OnSceneObjectSelected;
		
		// Check if Minecraft assets are loaded
		var loader = MinecraftJsonLoader.Instance;
		var textureLoader = MinecraftTextureLoader.Instance;
		var characterLoader = CharacterLoader.Instance;
		
		if (loader.IsLoaded)
		{
			GD.Print($"Main scene started with {loader.TotalFilesLoaded} Minecraft JSON files loaded.");
		}
		else
		{
			GD.PrintErr("Warning: Main scene started without Minecraft JSON files loaded!");
		}
		
		if (textureLoader.IsLoaded)
		{
			GD.Print($"Main scene started with {textureLoader.TotalTexturesLoaded} Minecraft textures loaded.");
			GD.Print($"  - Block textures: {textureLoader.GetAllBlockTexturePaths().Count()}");
			GD.Print($"  - Item textures: {textureLoader.GetAllItemTexturePaths().Count()}");
		}
		else
		{
			GD.PrintErr("Warning: Main scene started without Minecraft textures loaded!");
		}
		
		if (characterLoader.IsLoaded)
		{
			GD.Print($"Main scene started with {characterLoader.TotalCharactersFound} character GLB files found.");
		}
		else
		{
			GD.PrintErr("Warning: Main scene started without character files scanned!");
		}
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
			
			// Sync background from main viewport
			SyncPreviewBackground();
		}
	}

	public void SyncPreviewBackground()
	{
		if (PreviewViewportControl?.PreviewSubViewport == null || Viewport == null)
			return;
			
		// Get background elements from main viewport
		var mainCanvasLayer = Viewport.GetNodeOrNull<CanvasLayer>("CanvasLayer");
		var previewCanvasLayer = PreviewViewportControl.PreviewSubViewport.GetNodeOrNull<CanvasLayer>("CanvasLayer");
		
		if (mainCanvasLayer == null || previewCanvasLayer == null)
			return;
			
		// Sync background color
		var mainBgColor = mainCanvasLayer.GetNodeOrNull<ColorRect>("BackgroundColor");
		var previewBgColor = previewCanvasLayer.GetNodeOrNull<ColorRect>("BackgroundColor");
		
		if (mainBgColor != null && previewBgColor != null)
		{
			previewBgColor.Color = mainBgColor.Color;
		}
		
		// Sync background image
		var mainBgImage = mainCanvasLayer.GetNodeOrNull<TextureRect>("BackgroundImage");
		var previewBgImage = previewCanvasLayer.GetNodeOrNull<TextureRect>("BackgroundImage");
		
		if (mainBgImage != null && previewBgImage != null)
		{
			previewBgImage.Texture = mainBgImage.Texture;
			
			// Sync stretch mode settings
			previewBgImage.ExpandMode = mainBgImage.ExpandMode;
			previewBgImage.StretchMode = mainBgImage.StretchMode;
		}
	}

	private void SetupMenus()
	{
		//Setup File Menu
		var filePopup = FileButton.GetPopup();
		filePopup.AddItem("New Project", 0);
		filePopup.AddItem("Open Project", 1);
		filePopup.AddItem("Save Project", 2);
		filePopup.AddSeparator();
		filePopup.AddItem("Exit", 3);
		
		//Setup Edit Menu
		var editPopup = EditButton.GetPopup();
		editPopup.AddItem("Undo", 0);
		editPopup.AddItem("Redo", 1);
		filePopup.AddSeparator();
		editPopup.AddItem("Cut", 2);
		editPopup.AddItem("Copy", 3);
		editPopup.AddItem("Paste", 4);
		editPopup.AddItem("Delete", 5);
		//connect
		
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
		//connect
		
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
	
	private void OnSpawnButtonPressed()
	{
		// Show the spawn menu at the global position of the spawn button
		var menuPosition = SpawnButton.GlobalPosition;
		_spawnMenu.ShowMenu(menuPosition);
	}

	private void OnFileMenuPressed(int id)
	{
		switch (id)
		{
			case 3:
				GetTree().Quit();
				break;
		}
	}
	
	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			//TODO: Detect unsaved projects
			GetTree().Quit();
		}
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
}