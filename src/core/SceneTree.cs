using System.Linq;
using Godot;
using Godot.Collections;
using simplyRemadeNuxi.core.commands;

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
		Tree.FocusMode = FocusModeEnum.None;
		
		vbox.AddChild(Tree);
		
		// Setup context menu
		_contextMenu = new PopupMenu();
		_contextMenu.Name = "ContextMenu";
		_contextMenu.AddItem("Duplicate", 0);
		_contextMenu.AddItem("Delete", 1);
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

		if (Tree != null)
		{
			var root = Tree.GetRoot();
			if (root == null)
			{
				root = Tree.CreateItem();
				root.SetText(0, "Scene");
			}
		
			BuildTreeRecursively(root, Viewport);
		}
	}

	private void BuildTreeRecursively(TreeItem parentItem, Node viewport)
	{
		foreach (var child in viewport.GetChildren())
		{
			if (child is SceneObject sceneObject)
			{
				var item = Tree.CreateItem(parentItem);
				parentItem?.GetText(0);
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
		if (!dict.ContainsKey("scene_object"))
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
		if (!dict.ContainsKey("scene_object"))
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
			// Dropped on item - make it a child
			newParent = dropSection == 0 ? targetObject :
				// Dropped above/below item - use same parent as target
				targetObject.GetParent();
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

		// Capture old parent before reparenting (for undo)
		var oldParent = sceneObject.GetParent();

		// Reparent
		sceneObject.Reparent(newParent);
		
		// Restore global transform if shift was held
		if (globalTransform.HasValue)
		{
			sceneObject.GlobalTransform = globalTransform.Value;
		}

		// Record undo command
		if (EditorCommandHistory.Instance != null && oldParent != null && oldParent != newParent)
		{
			EditorCommandHistory.Instance.PushWithoutExecute(
				new ReparentCommand(sceneObject, oldParent, newParent));
		}

		// Rebuild tree to reflect changes
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		BuildTree();
		
		// Reselect the object
		SelectObject(sceneObject);

		sceneObject.UpdateVisualPosition();
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
		switch (index)
		{
			// Duplicate
			case 0:
			{
				if (_contextMenuItem != null && ObjectMap.TryGetValue(_contextMenuItem, out var sceneObject))
				{
					DuplicateObject(sceneObject);
					_contextMenuItem = null;
				}

				break;
			}
			// Delete
			case 1:
			{
				if (_contextMenuItem != null && ObjectMap.TryGetValue(_contextMenuItem, out var sceneObject))
				{
					DeleteObject(sceneObject);
					_contextMenuItem = null;
				}

				break;
			}
		}
	}

	private int GetNextAvailableObjectNumber()
	{
		var existingNumbers = new System.Collections.Generic.HashSet<int>();

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
	}

	/// <summary>
	/// Strips any trailing integer from a name and returns the base name.
	/// E.g. "Cube3" -> "Cube", "PointLight" -> "PointLight"
	/// </summary>
	private static string GetBaseName(string name)
	{
		int i = name.Length - 1;
		while (i >= 0 && char.IsDigit(name[i]))
			i--;
		// Only strip if there is at least one digit at the end and something before it
		if (i >= 0 && i < name.Length - 1)
			return name.Substring(0, i + 1);
		return name;
	}

	/// <summary>
	/// Returns the next available number for a given base name across all SceneObjects
	/// in the viewport, using the same convention as SpawnMenu (no suffix for 1, suffix
	/// "2", "3", … for subsequent instances).
	/// </summary>
	private int GetNextAvailableNameNumber(string baseName)
	{
		var existingNumbers = new System.Collections.Generic.HashSet<int>();

		if (Viewport != null)
			ScanNode(Viewport);

		int next = 1;
		while (existingNumbers.Contains(next))
			next++;
		return next;

		void ScanNode(Node node)
		{
			foreach (var child in node.GetChildren())
			{
				if (child is SceneObject so)
				{
					var n = so.Name.ToString();
					if (n == baseName)
					{
						existingNumbers.Add(1);
					}
					else if (n.StartsWith(baseName) && n.Length > baseName.Length)
					{
						var suffix = n.Substring(baseName.Length);
						if (int.TryParse(suffix, out int num))
							existingNumbers.Add(num);
					}
					ScanNode(so);
				}
			}
		}
	}

	/// <summary>
	/// Creates a shallow duplicate of a single SceneObject (no children), copies all
	/// custom C# properties, and adds it to <paramref name="parent"/>.
	/// Returns null if the type cannot be duplicated (e.g. CharacterSceneObject).
	/// </summary>
	private SceneObject CreateSceneObjectDuplicate(SceneObject original, Node parent)
	{
		SceneObject duplicate;

		switch (original)
		{
			case LightSceneObject originalLight:
			{
				var dup = new LightSceneObject();
				dup.LightColor = originalLight.LightColor;
				dup.LightEnergy = originalLight.LightEnergy;
				dup.LightRange = originalLight.LightRange;
				dup.LightIndirectEnergy = originalLight.LightIndirectEnergy;
				dup.LightSpecular = originalLight.LightSpecular;
				dup.LightShadowEnabled = originalLight.LightShadowEnabled;
				duplicate = dup;
				break;
			}
			case CameraSceneObject originalCamera:
			{
				var dup = new CameraSceneObject();
				dup.Fov = originalCamera.Fov;
				dup.Near = originalCamera.Near;
				dup.Far = originalCamera.Far;
				duplicate = dup;
				break;
			}
			case CharacterSceneObject:
				GD.PrintErr("[SceneTree] Duplicating CharacterSceneObject is not supported.");
				return null;
			default:
			{
				// Generic SceneObject – duplicate the Visual children so meshes/models are copied
				duplicate = new SceneObject();
				foreach (var child in original.Visual.GetChildren())
				{
					if (child is Node childNode)
						duplicate.Visual.AddChild((Node)childNode.Duplicate());
				}
				break;
			}
		}

		// Determine the name: strip trailing number from original, then find next available
		var baseName = GetBaseName(original.Name.ToString());
		var nextNum = GetNextAvailableNameNumber(baseName);
		duplicate.Name = nextNum > 1 ? $"{baseName}{nextNum}" : baseName;

		// Copy base SceneObject properties
		duplicate.ObjectType = original.ObjectType;
		duplicate.IsSelectable = original.IsSelectable;

		// Copy transform
		duplicate.Position = original.Position;
		duplicate.Rotation = original.Rotation;
		duplicate.Scale = original.Scale;

		// Copy pivot offset
		duplicate.PivotOffset = original.PivotOffset;

		// Copy visibility
		duplicate.SetObjectVisible(original.ObjectVisible);

		// Deep-copy keyframes
		foreach (var kvp in original.Keyframes)
		{
			var copiedFrames = kvp.Value.Select(kf => new ObjectKeyframe { Frame = kf.Frame, Value = kf.Value, InterpolationType = kf.InterpolationType }).ToList();
			duplicate.Keyframes[kvp.Key] = copiedFrames;
		}

		// Add to the requested parent
		parent.AddChild(duplicate);
		return duplicate;
	}

	/// <summary>
	/// Recursively duplicates <paramref name="original"/> and all of its child
	/// SceneObjects, parenting the root duplicate to the same parent as the original.
	/// Returns the root duplicate, or null if the type is unsupported.
	/// </summary>
	private SceneObject DuplicateObjectRecursive(SceneObject original, Node parent)
	{
		var duplicate = CreateSceneObjectDuplicate(original, parent);
		if (duplicate == null)
			return null;

		// Recursively duplicate child SceneObjects
		foreach (var child in original.GetChildren())
		{
			if (child is SceneObject childSceneObject)
				DuplicateObjectRecursive(childSceneObject, duplicate);
		}

		return duplicate;
	}

	/// <summary>
	/// Duplicates the given SceneObject (and its child SceneObjects), then rebuilds
	/// the scene tree and selects the new root duplicate.
	/// </summary>
	private async void DuplicateObject(SceneObject original)
	{
		var parent = original.GetParent();
		var duplicate = DuplicateObjectRecursive(original, parent);
		if (duplicate == null)
			return;

		// Rebuild tree and select the new duplicate
		await ToSignal(GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
		BuildTree();

		SelectionManager.Instance.ClearSelection();
		SelectionManager.Instance.SelectObject(duplicate);
	}

	/// <summary>
	/// Duplicates all currently selected objects. Called from Ctrl+D shortcut.
	/// </summary>
	public void DuplicateSelectedObjects()
	{
		var selected = new System.Collections.Generic.List<SceneObject>(
			SelectionManager.Instance.SelectedObjects);
		foreach (var obj in selected)
		{
			DuplicateObject(obj);
		}
	}

	private void DeleteObject(SceneObject sceneObject)
	{
		// Capture the parent before removing so the command can restore it
		var parent = sceneObject.GetParent();

		// Deselect before removing
		SelectionManager.Instance.ClearSelection();

		// Use the command so the deletion can be undone
		if (EditorCommandHistory.Instance != null && parent != null)
		{
			EditorCommandHistory.Instance.Execute(new DeleteObjectCommand(sceneObject, parent));
		}
		else
		{
			// Fallback: no history available, just remove permanently
			if (parent != null)
				parent.RemoveChild(sceneObject);
			sceneObject.QueueFree();
		}

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