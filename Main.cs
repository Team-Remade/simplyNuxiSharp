using Gizmo3DPlugin;
using Godot;
using simplyRemadeNuxi.core;
using SceneTree = simplyRemadeNuxi.core.SceneTree;

namespace simplyRemadeNuxi;

public partial class Main : Control
{
	public RandomNumberGenerator Random = new RandomNumberGenerator();
	
	[Export] public MenuButton FileButton;
	[Export] public MenuButton EditButton;
	[Export] public MenuButton ViewButton;
	[Export] public MenuButton RenderButton;
	[Export] public MenuButton HelpButton;
	
	[Export] public SubViewport Viewport;
	[Export] public SceneTree SceneTreePanel;
	[Export] public Control SceneTree;
	[Export] public ObjectPropertiesPanel ObjectPropertyPanel;
	
	public override void _EnterTree()
	{
		SetWindowTitle("Mine Imator Simply Remade: Nuxi");
	}

	public override async void _Ready()
	{
		SetupMenus();
		SceneTreePanel.SetViewport(Viewport);
		
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		
		SetupGizmo();
		
		SceneTreePanel.ObjectSelected += OnSceneObjectSelected;
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

	private void OnSceneObjectSelected(SceneObject sceneObject)
	{
		SelectionManager.Instance.ClearSelection();
		SelectionManager.Instance.SelectObject(sceneObject);
	}
}