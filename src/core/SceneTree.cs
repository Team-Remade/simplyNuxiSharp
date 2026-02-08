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
	private PopupMenu _contextMenu;
	private TreeItem _contextMenuItem;
	
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
		
		Tree = new Tree();
		Tree.Name = "Tree";
		Tree.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		Tree.SizeFlagsVertical = SizeFlags.ExpandFill;
		Tree.Columns = 1;
		Tree.HideRoot = true;
		Tree.SelectMode = Tree.SelectModeEnum.Single;
		Tree.AllowReselect = true;
		Tree.AllowRmbSelect = true; // Enable right-click selection
		Tree.DropModeFlags = (int)Tree.DropModeFlagsEnum.OnItem | (int)Tree.DropModeFlagsEnum.Inbetween;
		
		vbox.AddChild(Tree);
		
		// Setup context menu
		_contextMenu = new PopupMenu();
		_contextMenu.Name = "ContextMenu";
		_contextMenu.AddItem("Delete", 0);
		_contextMenu.IndexPressed += OnContextMenuIndexPressed;
		AddChild(_contextMenu);
		
		//Connect Tree
		Tree.ItemSelected += OnItemSelected;
		Tree.ItemEdited += OnItemEdited;
		Tree.ItemActivated += OnItemActivated;
		Tree.ItemCollapsed += OnItemCollapsed;
		Tree.ItemMouseSelected += OnItemMouseSelected;
		
		// Setup drag and drop
		Tree.SetDragForwarding(
			Callable.From<Vector2, Variant>(GetDragData),
			Callable.From<Vector2, Variant, bool>(CanDropData),
			Callable.From<Vector2, Variant>(DropData)
		);
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
	
	private Variant GetDragData(Vector2 position)
	{
		var item = Tree.GetItemAtPosition(position);
		if (item != null && ObjectMap.TryGetValue(item, out var sceneObject))
		{
			// Create drag preview
			var preview = new Label();
			preview.Text = sceneObject.GetDisplayName();
			preview.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.8f));
			Tree.SetDragPreview(preview);
			
			// Return the scene object as drag data
			return Variant.From(new Dictionary() { { "scene_object", sceneObject }, { "tree_item", item } });
		}
		return default;
	}
	
	private bool CanDropData(Vector2 position, Variant data)
	{
		var dict = data.AsGodotDictionary();
		if (dict == null || !dict.ContainsKey("scene_object"))
			return false;
		
		var draggedObject = dict["scene_object"].As<SceneObject>();
		if (draggedObject == null)
			return false;
		
		var targetItem = Tree.GetItemAtPosition(position);
		
		// Can drop between items or on items
		if (targetItem == null)
		{
			// Allow dropping at root level
			return true;
		}
		
		// Get target object
		if (ObjectMap.TryGetValue(targetItem, out var targetObject))
		{
			// Can't drop on self
			if (draggedObject == targetObject)
				return false;
			
			// Can't drop on a child of self (would create circular dependency)
			if (IsDescendantOf(targetObject, draggedObject))
				return false;
			
			return true;
		}
		
		return false;
	}
	
	private void DropData(Vector2 position, Variant data)
	{
		var dict = data.AsGodotDictionary();
		if (dict == null || !dict.ContainsKey("scene_object"))
			return;
		
		var draggedObject = dict["scene_object"].As<SceneObject>();
		if (draggedObject == null)
			return;
		
		var targetItem = Tree.GetItemAtPosition(position);
		var dropSection = Tree.GetDropSectionAtPosition(position);
		
		// Check if shift is held to preserve global transform
		bool preserveGlobalTransform = Input.IsKeyPressed(Key.Shift);
		
		// Determine new parent based on drop position
		Node newParent = null;
		
		if (targetItem == null)
		{
			// Dropped in empty space, reparent to viewport root
			newParent = Viewport;
		}
		else if (ObjectMap.TryGetValue(targetItem, out var targetObject))
		{
			if (dropSection == 0)
			{
				// Dropped on item - make it a child
				newParent = targetObject;
			}
			else
			{
				// Dropped above/below item - use same parent as target
				newParent = targetObject.GetParent();
			}
		}
		
		if (newParent != null && newParent != draggedObject.GetParent())
		{
			// Reparent the object
			ReparentObject(draggedObject, newParent, preserveGlobalTransform);
		}
	}
	
	private bool IsDescendantOf(Node potentialDescendant, Node potentialAncestor)
	{
		var parent = potentialDescendant.GetParent();
		while (parent != null)
		{
			if (parent == potentialAncestor)
				return true;
			parent = parent.GetParent();
		}
		return false;
	}
	
	private async void ReparentObject(SceneObject sceneObject, Node newParent, bool preserveGlobalTransform)
	{
		Transform3D? globalTransform = null;
		
		// Only store global transform if shift is held
		if (preserveGlobalTransform)
		{
			globalTransform = sceneObject.GlobalTransform;
		}
		
		// Reparent
		sceneObject.Reparent(newParent);
		
		// Restore global transform if shift was held
		if (globalTransform.HasValue)
		{
			sceneObject.GlobalTransform = globalTransform.Value;
		}
		
		// Rebuild tree to reflect changes
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		BuildTree();
		
		// Reselect the object
		SelectObject(sceneObject);
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
	
	private void OnItemMouseSelected(Vector2 position, long mouseButtonIndex)
	{
		// Right-click detected
		if (mouseButtonIndex == (long)MouseButton.Right)
		{
			var selected = Tree.GetSelected();
			if (selected != null && ObjectMap.ContainsKey(selected))
			{
				_contextMenuItem = selected;
				// Show context menu using screen position
				var screenPos = Tree.GetScreenPosition() + position;
				_contextMenu.Position = (Vector2I)screenPos;
				_contextMenu.Popup();
			}
		}
	}
	
	private void OnContextMenuIndexPressed(long index)
	{
		if (index == 0) // Delete
		{
			if (_contextMenuItem != null && ObjectMap.TryGetValue(_contextMenuItem, out var sceneObject))
			{
				DeleteObject(sceneObject);
				_contextMenuItem = null;
			}
		}
	}

	private int GetNextAvailableObjectNumber()
	{
		var existingNumbers = new System.Collections.Generic.HashSet<int>();
		
		// Scan all existing SceneObjects to find used numbers
		void ScanNode(Node node)
		{
			foreach (var child in node.GetChildren())
			{
				if (child is SceneObject sceneObject)
				{
					// Try to extract number from name like "Object1", "Object2", etc.
					var name = sceneObject.Name.ToString();
					if (name.StartsWith("Object") && name.Length > 6)
					{
						var numberPart = name.Substring(6);
						if (int.TryParse(numberPart, out int num))
						{
							existingNumbers.Add(num);
						}
					}
					
					// Recursively scan children
					ScanNode(sceneObject);
				}
			}
		}
		
		if (Viewport != null)
		{
			ScanNode(Viewport);
		}
		
		// Find the lowest available number starting from 1
		int nextNumber = 1;
		while (existingNumbers.Contains(nextNumber))
		{
			nextNumber++;
		}
		
		return nextNumber;
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