using Godot;
using Godot.Collections;

namespace simplyRemadeNuxi.core;

public partial class SceneTree : Panel
{
	[Signal] public delegate void ObjectSelectedEventHandler(SceneObject sceneObject);
	[Signal] public delegate void ObjectRenamedEventHandler(SceneObject sceneObject, string newName);

	private Node Viewport;
	
	public Tree Tree;
	public Dictionary<TreeItem, SceneObject> ObjectMap = new Dictionary<TreeItem, SceneObject>();
	
	private bool _isProcessingSelection = false;
	
	const string ItemMetaObject = "SceneObject";
	const string ItemMetaExpanded = "IsExpanded";
	
	public async void SetViewport(Node viewport)
	{
		Viewport = viewport;
		if (IsInsideTree())
		{
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
			BuildTree();
		}
	}

	public override async void _Ready()
	{
		SetupUi();
		if (Viewport == null)
		{
			await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		}
		BuildTree();
		
		// Connect to SelectionManager to sync with viewport selection
		if (SelectionManager.Instance != null)
		{
			SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
		}
	}

	private void SetupUi()
	{
		var vbox = new VBoxContainer();
		vbox.Name = "VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 0);
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(vbox);
		
		var toolbox = new HBoxContainer();
		toolbox.Name = "Toolbar";
		toolbox.CustomMinimumSize = new Vector2(0, 24);
		toolbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(toolbox);

		var addBtn = new Button();
		addBtn.Name = "AddButton";
		addBtn.Text = "+";
		addBtn.TooltipText = "Add Object";
		addBtn.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		toolbox.AddChild(addBtn);
		
		var deleteBtn = new Button();
		deleteBtn.Name = "DeleteButton";
		deleteBtn.Text = "-";
		deleteBtn.TooltipText = "Delete Object";
		deleteBtn.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		toolbox.AddChild(deleteBtn);

		//Connect Buttons
		addBtn.Pressed += OnAddPressed;
		deleteBtn.Pressed += OnRemovePressed;
		
		Tree = new Tree();
		Tree.Name = "Tree";
		Tree.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		Tree.SizeFlagsVertical = SizeFlags.ExpandFill;
		Tree.Columns = 1;
		Tree.HideRoot = true;
		Tree.SelectMode = Tree.SelectModeEnum.Single;
		Tree.AllowReselect = true;
		
		vbox.AddChild(Tree);
		
		//Connect Tree
		Tree.ItemSelected += OnItemSelected;
		Tree.ItemEdited += OnItemEdited;
		Tree.ItemActivated += OnItemActivated;
		Tree.ItemCollapsed += OnItemCollapsed;
	}

	private Control CreateDraggingPreview()
	{
		var preview = new Label();
		preview.Text = "Dragging...";
		preview.Modulate = new Color(1, 1, 1, 0.8f);
		return preview;
	}

	private void BuildTree()
	{
		Tree?.Clear();
		ObjectMap.Clear();

		if (Viewport == null) return;

		var root = Tree.GetRoot();
		if (root == null)
		{
			root = Tree.CreateItem();
			root.SetText(0, "Scene");
		}
		
		BuildTreeRecursively(root, Viewport);
	}

	private void BuildTreeRecursively(TreeItem parentItem, Node viewport)
	{
		foreach (var child in viewport.GetChildren())
		{
			if (child is SceneObject sceneObject)
			{
				var item = Tree.CreateItem(parentItem);
				var parentName = "<empty>";
				if (parentItem != null)
				{
					parentName = parentItem.GetText(0);
				}
				SetupTreeItem(item, sceneObject);
				ObjectMap[item] = sceneObject;
				
				//Recursive function
				BuildTreeRecursively(item, sceneObject);
			}
		}
	}

	private void SetupTreeItem(TreeItem item, SceneObject sceneObject)
	{
		item.SetText(0, sceneObject.GetDisplayName());
		item.SetMetadata(0, new Dictionary() { { ItemMetaObject, sceneObject } });
		
		//TODO: Icons
		
		// Mark selected items
		if (sceneObject.IsSelected)
		{
			item.Select(0);
		}
	}

	private void OnItemSelected()
	{
		if (_isProcessingSelection) return;
		
		var selected = Tree.GetSelected();
		if (selected != null && ObjectMap.TryGetValue(selected, out var item))
		{
			EmitSignal(nameof(ObjectSelected), item);
			SelectionManager.Instance.ClearSelection();
			SelectionManager.Instance.SelectObject(item);
		}
	}
	
	private void OnItemActivated()
	{
		//Double click to expand/collapse
		var selected = Tree.GetSelected();
		if (selected != null)
		{
			selected.Collapsed = !selected.Collapsed;
		}
	}
	
	private void OnItemEdited()
	{
		//Rename
		var selected = Tree.GetSelected();
		if (selected != null && ObjectMap.TryGetValue(selected, out var item))
		{
			var newName = selected.GetText(0);
			if (newName != item.Name && newName.StripEdges() != "")
			{
				item.Name = newName;
				EmitSignal(nameof(ObjectRenamed), item);
			}
		}
	}
	
	private void OnItemCollapsed(TreeItem item)
	{
		var meta = (Dictionary)item.GetMetadata(0);
		meta[ItemMetaExpanded] = item.Collapsed;
	}

	private void OnAddPressed()
	{
		if (Viewport != null)
		{
			var obj = new SceneObject();
			obj.Name = "Object" + obj.ObjectId;
			Viewport.AddChild(obj);
			var material = new StandardMaterial3D();
			var meshInstance = new MeshInstance3D();
			var mesh = new BoxMesh();
			mesh.SurfaceSetMaterial(0, material);
			meshInstance.Mesh = mesh;
			obj.AddVisualInstance(meshInstance);
			BuildTree();
			_isProcessingSelection = true;
			SelectObject(obj);
			_isProcessingSelection = false;
		}
	}

	private void OnRemovePressed()
	{
		var selected = Tree.GetSelected();
		if (selected != null && ObjectMap.TryGetValue(selected, out var item))
		{
			DeleteObject(item);
		}
	}

	private async void DeleteObject(SceneObject sceneObject)
	{
		foreach (var child in sceneObject.GetChildren())
		{
			if (child is SceneObject childObject)
			{
				DeleteObject(childObject);
			}
		}
		
		SelectionManager.Instance.ClearSelection();
		
		sceneObject.QueueFree();
		
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		BuildTree();
	}

	private void OnSelectionChanged()
	{
		if (_isProcessingSelection) return;
		
		// Update tree selection to match viewport selection
		var selectedObject = SelectionManager.Instance.SelectedObjects.Count > 0 
			? SelectionManager.Instance.SelectedObjects[0] 
			: null;
		
		_isProcessingSelection = true;
		
		if (selectedObject != null)
		{
			SelectObject(selectedObject);
		}
		else
		{
			// Deselect all items in tree
			foreach (var item in ObjectMap.Keys)
			{
				item.Deselect(0);
			}
		}
		
		_isProcessingSelection = false;
	}

	private void SelectObject(SceneObject obj)
	{
		foreach (var keyPair in ObjectMap)
		{
			if (keyPair.Value != obj) continue;
			keyPair.Key.Select(0);
			Tree.ScrollToItem(keyPair.Key);
			break;
		}
	}

	public void Refresh()
	{
		BuildTree();
	}
	
	public void RefreshObject(SceneObject obj)
	{
		foreach (var keyPair in ObjectMap)
		{
			if (keyPair.Value != obj) continue;
			SetupTreeItem(keyPair.Key, obj);
			break;
		}
	}
}