using Godot;
using System.Collections.Generic;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.ui;

public partial class SpawnMenu : PopupPanel
{
	private ItemList _categoryList;
	private ItemList _objectList;
	private Button _spawnButton;
	private Dictionary<string, List<string>> _categories;
	private string _selectedCategory = "Primitives";
	private int _selectedObjectIndex = -1;
	
	public SubViewport Viewport { get; set; }

	public override void _Ready()
	{
		SetSize(new Vector2I(600, 400));
		
		// Create overall vertical container
		var overallContainer = new VBoxContainer();
		overallContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(overallContainer);
		
		// Create main container for categories and objects
		var mainContainer = new HBoxContainer();
		mainContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		overallContainer.AddChild(mainContainer);
		
		// Left column - Categories
		var leftContainer = new VBoxContainer();
		leftContainer.CustomMinimumSize = new Vector2(200, 0);
		mainContainer.AddChild(leftContainer);
		
		var categoryLabel = new Label();
		categoryLabel.Text = "Categories";
		categoryLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
		leftContainer.AddChild(categoryLabel);
		
		_categoryList = new ItemList();
		_categoryList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_categoryList.ItemSelected += OnCategorySelected;
		leftContainer.AddChild(_categoryList);
		
		// Right column - Objects
		var rightContainer = new VBoxContainer();
		rightContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		mainContainer.AddChild(rightContainer);
		
		var objectLabel = new Label();
		objectLabel.Text = "Objects";
		objectLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
		rightContainer.AddChild(objectLabel);
		
		_objectList = new ItemList();
		_objectList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_objectList.ItemActivated += OnObjectDoubleClicked;
		_objectList.ItemSelected += OnObjectListItemSelected;
		rightContainer.AddChild(_objectList);
		
		// Add bottom container with spawn button
		var bottomContainer = new HBoxContainer();
		bottomContainer.AddThemeConstantOverride("separation", 10);
		overallContainer.AddChild(bottomContainer);
		
		// Add spacer to push button to the right
		var spacer = new Control();
		spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		bottomContainer.AddChild(spacer);
		
		// Add spawn button
		_spawnButton = new Button();
		_spawnButton.Text = "Spawn";
		_spawnButton.CustomMinimumSize = new Vector2(100, 30);
		_spawnButton.Disabled = true;
		_spawnButton.Pressed += OnSpawnButtonPressed;
		bottomContainer.AddChild(_spawnButton);
		
		// Initialize categories and objects
		InitializeCategories();
		PopulateCategoryList();
		UpdateObjectList(_selectedCategory);
	}

	private void InitializeCategories()
	{
		_categories = new Dictionary<string, List<string>>()
		{
			{ "Primitives", new List<string>() 
				{ 
					"Cube", 
					"Sphere", 
					"Cylinder", 
					"Cone", 
					"Torus", 
					"Plane",
					"Capsule"
				} 
			}
		};
	}

	private void PopulateCategoryList()
	{
		_categoryList.Clear();
		foreach (var category in _categories.Keys)
		{
			_categoryList.AddItem(category);
		}
		
		// Select the first category by default
		if (_categoryList.ItemCount > 0)
		{
			_categoryList.Select(0);
		}
	}

	private void OnCategorySelected(long index)
	{
		var categoryName = _categoryList.GetItemText((int)index);
		UpdateObjectList(categoryName);
	}

	private void UpdateObjectList(string categoryName)
	{
		_objectList.Clear();
		
		if (_categories.ContainsKey(categoryName))
		{
			foreach (var objectName in _categories[categoryName])
			{
				_objectList.AddItem(objectName);
			}
		}
		
		_selectedCategory = categoryName;
	}

	private void OnObjectListItemSelected(long index)
	{
		_selectedObjectIndex = (int)index;
		_spawnButton.Disabled = false;
	}
	
	private void OnObjectDoubleClicked(long index)
	{
		_selectedObjectIndex = (int)index;
		SpawnSelectedObject();
	}
	
	private void OnSpawnButtonPressed()
	{
		SpawnSelectedObject();
	}
	
	private void SpawnSelectedObject()
	{
		if (_selectedObjectIndex >= 0 && _selectedObjectIndex < _objectList.ItemCount)
		{
			var objectName = _objectList.GetItemText(_selectedObjectIndex);
			SpawnObject(objectName);
			Hide();
		}
	}

	private void SpawnObject(string objectName)
	{
		if (Viewport == null)
		{
			GD.PrintErr("Viewport not set for SpawnMenu");
			return;
		}
		
		GD.Print($"Spawning object: {objectName}");
		
		// Get the next available number for this object type
		int nextNumber = GetNextAvailableObjectNumber(objectName);
		string fullObjectName = nextNumber > 1 ? $"{objectName}{nextNumber}" : objectName;
		
		// Create a new SceneObject
		var sceneObject = new SceneObject();
		sceneObject.Name = fullObjectName;
		sceneObject.ObjectType = objectName;
		
		// Create the appropriate mesh
		var meshInstance = new MeshInstance3D();
		var mesh = CreatePrimitiveMesh(objectName);
		
		if (mesh != null)
		{
			meshInstance.Mesh = mesh;
			
			// Create a material for the mesh
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
			meshInstance.MaterialOverride = material;
			
			// Set shadows
			meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
			
			// Add the mesh to the scene object's visual
			sceneObject.AddVisualInstance(meshInstance);
			
			// Add to viewport
			Viewport.AddChild(sceneObject);
			
			// Position at world origin for now
			sceneObject.GlobalPosition = Vector3.Zero;
			
			// Notify the scene tree panel to refresh
			if (GetTree().Root.GetNode<Main>("/root/Main") is Main main)
			{
				main.SceneTreePanel.Refresh();
			}
		}
		else
		{
			sceneObject.QueueFree();
			GD.PrintErr($"Could not create mesh for {objectName}");
		}
	}
	
	private int GetNextAvailableObjectNumber(string objectType)
	{
		var existingNumbers = new System.Collections.Generic.HashSet<int>();
		
		// Scan all existing SceneObjects to find used numbers
		void ScanNode(Node node)
		{
			foreach (var child in node.GetChildren())
			{
				if (child is SceneObject sceneObject)
				{
					var name = sceneObject.Name.ToString();
					
					// Check if name starts with our object type
					if (name == objectType)
					{
						// First instance without number (e.g., "Cube")
						existingNumbers.Add(1);
					}
					else if (name.StartsWith(objectType) && name.Length > objectType.Length)
					{
						// Try to extract number from name like "Cube2", "Cube3", etc.
						var numberPart = name.Substring(objectType.Length);
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
	
	private Mesh CreatePrimitiveMesh(string primitiveType)
	{
		return primitiveType switch
		{
			"Cube" => new BoxMesh(),
			"Sphere" => new SphereMesh(),
			"Cylinder" => new CylinderMesh(),
			"Cone" => new CylinderMesh { TopRadius = 0.0f, BottomRadius = 1.0f },
			"Torus" => new TorusMesh(),
			"Plane" => new PlaneMesh { Size = new Vector2(2, 2) },
			"Capsule" => new CapsuleMesh(),
			_ => null
		};
	}

	public void ShowMenu(Vector2 position)
	{
		// Set popup position and size
		var rect = new Rect2I((Vector2I)position, new Vector2I(600, 400));
		PopupOnParent(rect);
	}
}
