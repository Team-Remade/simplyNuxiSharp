using Godot;
using System.Collections.Generic;
using System.Linq;
using simplyRemadeNuxi.core;

namespace simplyRemadeNuxi.ui;

public partial class SpawnMenu : PopupPanel
{
	private ItemList _categoryList;
	private ItemList _objectList;
	private ItemList _variantList;
	private Button _spawnButton;
	private LineEdit _searchBar;
	private Dictionary<string, List<string>> _categories;
	private string _selectedCategory = "Primitives";
	private int _selectedObjectIndex = -1;
	private int _selectedVariantIndex = -1;
	private string _selectedBlockState = "";
	private string _searchQuery = "";
	
	public SubViewport Viewport { get; set; }

	public override void _Ready()
	{
		SetSize(new Vector2I(900, 400));
		
		// Create overall vertical container
		var overallContainer = new VBoxContainer();
		overallContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(overallContainer);
		
		// Add search bar at the top
		var searchContainer = new HBoxContainer();
		searchContainer.AddThemeConstantOverride("separation", 10);
		overallContainer.AddChild(searchContainer);
		
		var searchLabel = new Label();
		searchLabel.Text = "Search:";
		searchContainer.AddChild(searchLabel);
		
		_searchBar = new LineEdit();
		_searchBar.PlaceholderText = "Type to search objects...";
		_searchBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_searchBar.TextChanged += OnSearchTextChanged;
		searchContainer.AddChild(_searchBar);
		
		var clearButton = new Button();
		clearButton.Text = "Clear";
		clearButton.Pressed += OnClearSearchPressed;
		searchContainer.AddChild(clearButton);
		
		// Create main container for categories, objects, and variants
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
		
		// Middle column - Objects/Blocks
		var middleContainer = new VBoxContainer();
		middleContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		mainContainer.AddChild(middleContainer);
		
		var objectLabel = new Label();
		objectLabel.Text = "Objects";
		objectLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
		middleContainer.AddChild(objectLabel);
		
		_objectList = new ItemList();
		_objectList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_objectList.ItemActivated += OnObjectDoubleClicked;
		_objectList.ItemSelected += OnObjectListItemSelected;
		middleContainer.AddChild(_objectList);
		
		// Right column - Variants
		var rightContainer = new VBoxContainer();
		rightContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rightContainer.CustomMinimumSize = new Vector2(250, 0);
		mainContainer.AddChild(rightContainer);
		
		var variantLabel = new Label();
		variantLabel.Text = "Variants";
		variantLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
		rightContainer.AddChild(variantLabel);
		
		_variantList = new ItemList();
		_variantList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_variantList.ItemActivated += OnVariantDoubleClicked;
		_variantList.ItemSelected += OnVariantListItemSelected;
		rightContainer.AddChild(_variantList);
		
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
		
		// Add Blocks category from loaded Minecraft models
		LoadMinecraftBlocks();
	}
	
	private void LoadMinecraftBlocks()
	{
		var loader = MinecraftJsonLoader.Instance;
		if (!loader.IsLoaded)
		{
			GD.PrintErr("Cannot load Minecraft blocks - JSON files not loaded yet!");
			return;
		}
		
		var blocks = new List<string>();
		
		// Get all blockstate paths
		foreach (var blockStatePath in loader.GetAllBlockStatePaths())
		{
			// Look for blockstates in the "blockstates" directory
			if (blockStatePath.Contains("blockstates", System.StringComparison.OrdinalIgnoreCase))
			{
				// Extract the block name from the path
				var fileName = System.IO.Path.GetFileNameWithoutExtension(blockStatePath);
				
				// Clean up the name for display
				var displayName = CleanBlockName(fileName);
				if (!blocks.Contains(displayName))
				{
					blocks.Add(displayName);
				}
			}
		}
		
		// Sort alphabetically for easier browsing
		blocks.Sort();
		
		if (blocks.Count > 0)
		{
			_categories["Blocks"] = blocks;
			GD.Print($"Added {blocks.Count} Minecraft blocks to spawn menu from blockstates");
		}
		else
		{
			GD.Print("No Minecraft blockstates found");
		}
	}
	
	private string CleanBlockName(string fileName)
	{
		// Convert underscores to spaces and capitalize words
		var cleaned = fileName.Replace("_", " ");
		var words = cleaned.Split(' ');
		for (int i = 0; i < words.Length; i++)
		{
			if (words[i].Length > 0)
			{
				words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
			}
		}
		return string.Join(" ", words);
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
				// Filter based on search query
				if (string.IsNullOrEmpty(_searchQuery) || 
				    objectName.Contains(_searchQuery, System.StringComparison.OrdinalIgnoreCase))
				{
					_objectList.AddItem(objectName);
				}
			}
		}
		
		_selectedCategory = categoryName;
	}
	
	private void OnSearchTextChanged(string newText)
	{
		_searchQuery = newText;
		UpdateObjectList(_selectedCategory);
	}
	
	private void OnClearSearchPressed()
	{
		_searchBar.Text = "";
		_searchQuery = "";
		UpdateObjectList(_selectedCategory);
	}

	private void OnObjectListItemSelected(long index)
	{
		_selectedObjectIndex = (int)index;
		_selectedVariantIndex = -1;
		
GD.Print($"Selected object index: {index}, category: {_selectedCategory}");
		
		// Update variants list if this is a block
		if (_selectedCategory == "Blocks")
		{
			var objectName = _objectList.GetItemText((int)index);
			GD.Print($"Selected block: {objectName}");
			UpdateVariantList(objectName);
			// Don't set disabled here - let UpdateVariantList handle it
		}
		else
		{
			// For primitives, no variant selection needed
			_variantList.Clear();
			_spawnButton.Disabled = false;
			GD.Print("Primitive selected - button enabled");
		}
	}
	
	private void OnObjectDoubleClicked(long index)
	{
		_selectedObjectIndex = (int)index;
		
		// For non-block items, spawn immediately
		if (_selectedCategory != "Blocks")
		{
			SpawnSelectedObject();
		}
	}
	
	private void OnVariantListItemSelected(long index)
	{
		_selectedVariantIndex = (int)index;
		_spawnButton.Disabled = false;
	}
	
	private void OnVariantDoubleClicked(long index)
	{
		_selectedVariantIndex = (int)index;
		SpawnSelectedObject();
	}
	
	private void UpdateVariantList(string blockName)
	{
		_variantList.Clear();
		
		// Convert display name back to file name format
		var fileName = blockName.ToLower().Replace(" ", "_");
		
		GD.Print($"Loading variants for blockstate: {fileName}");
		
		// Get variants for this blockstate
		var variants = MinecraftModelHelper.GetBlockStateVariants(fileName);
		
		GD.Print($"Found {variants?.Count ?? 0} variants");
		
		if (variants != null && variants.Count > 0)
		{
			foreach (var variant in variants)
			{
				// Display the variant, or "Default" if empty string
				var displayName = string.IsNullOrEmpty(variant) ? "Default" : variant;
				_variantList.AddItem(displayName);
			}
			
			// Auto-select first variant and enable spawn button
			if (_variantList.ItemCount > 0)
			{
				_variantList.Select(0);
				_selectedVariantIndex = 0;
				_spawnButton.Disabled = false;
				GD.Print("Spawn button enabled with variant auto-selected");
			}
		}
		else
		{
			_variantList.AddItem("(No variants found)");
			_spawnButton.Disabled = true;
			GD.Print("No variants found - spawn button disabled");
		}
		
		_selectedBlockState = fileName;
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
		
		Node3D visualNode = null;
		
		// Check if this is a Minecraft block
		if (_selectedCategory == "Blocks")
		{
			visualNode = CreateMinecraftBlock(objectName);
		}
		else
		{
			// Create primitive mesh
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
				
				visualNode = meshInstance;
			}
		}
		
		if (visualNode != null)
		{
			// Add the visual to the scene object
			sceneObject.AddVisualInstance(visualNode);
			
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
			GD.PrintErr($"Could not create visual for {objectName}");
		}
	}
	
	private Node3D CreateMinecraftBlock(string blockName)
	{
		// Get the selected variant
		string variant = "";
		if (_selectedVariantIndex >= 0 && _selectedVariantIndex < _variantList.ItemCount)
		{
			var variantName = _variantList.GetItemText(_selectedVariantIndex);
			// If it says "Default", use empty string
			variant = variantName == "Default" ? "" : variantName;
		}
		
		// Create the node from the blockstate
		var blockNode = MinecraftModelHelper.CreateNodeFromBlockState(_selectedBlockState, variant);
		
		if (blockNode != null)
		{
			GD.Print($"Successfully created Minecraft block: {blockName} (variant: {(string.IsNullOrEmpty(variant) ? "default" : variant)})");
			return blockNode;
		}
		
		GD.PrintErr($"Failed to create Minecraft block: {blockName}");
		return null;
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
		var rect = new Rect2I((Vector2I)position, new Vector2I(900, 400));
		PopupOnParent(rect);
	}
}
