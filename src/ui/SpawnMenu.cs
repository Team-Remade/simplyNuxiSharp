using Godot;
using System.Collections.Generic;
using System.Linq;
using simplyRemadeNuxi.core;
using simplyRemadeNuxi.core.commands;

namespace simplyRemadeNuxi.ui;

public partial class SpawnMenu : PopupPanel
{
    private ItemList _categoryList;
    private ItemList _objectList;
    private ItemList _variantList;
    private Button _spawnButton;
    private LineEdit _searchBar;
    private OptionButton _textureTypeDropdown;
    private Label _dropdownLabel;
    private CheckBox _spawn3DPlaneCheckbox;
    private Dictionary<string, List<string>> _categories;
    private string _selectedCategory = "Primitives";
    private int _selectedObjectIndex = -1;
    private int _selectedVariantIndex = -1;
    private string _selectedBlockState = "";
    private string _searchQuery = "";
    private string _selectedTextureType = "item"; // "block" or "item"

    // Custom model history (in-memory only - per project when project system is implemented)
    private List<string> _customModelHistory = new List<string>();

    private Dictionary<string, string>
        _customModelPaths = new Dictionary<string, string>(); // Display name -> full path

    // Model loading progress window
    private ModelLoadingProgressWindow _modelLoadingWindow;

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

        // Add header with dropdown for texture type
        var middleHeaderContainer = new HBoxContainer();
        middleHeaderContainer.AddThemeConstantOverride("separation", 10);
        middleContainer.AddChild(middleHeaderContainer);

        var objectLabel = new Label();
        objectLabel.Text = "Objects";
        objectLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
        objectLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        middleHeaderContainer.AddChild(objectLabel);

        // Add dropdown label and dropdown (initially hidden)
        _dropdownLabel = new Label();
        _dropdownLabel.Text = "Type:";
        _dropdownLabel.Visible = false;
        middleHeaderContainer.AddChild(_dropdownLabel);

        _textureTypeDropdown = new OptionButton();
        _textureTypeDropdown.AddItem("Block");
        _textureTypeDropdown.AddItem("Item");
        _textureTypeDropdown.Selected = 1;
        _textureTypeDropdown.ItemSelected += OnTextureTypeSelected;
        _textureTypeDropdown.Visible = false;
        middleHeaderContainer.AddChild(_textureTypeDropdown);

        // Add 3D plane checkbox (initially hidden)
        _spawn3DPlaneCheckbox = new CheckBox();
        _spawn3DPlaneCheckbox.Text = "3D Plane";
        _spawn3DPlaneCheckbox.ButtonPressed = true; // Checked by default
        _spawn3DPlaneCheckbox.Visible = false;
        middleHeaderContainer.AddChild(_spawn3DPlaneCheckbox);

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

        // Initialize model loading progress window
        InitializeModelLoadingWindow();
    }

    private void InitializeModelLoadingWindow()
    {
        // Create the model loading progress window
        _modelLoadingWindow = new ModelLoadingProgressWindow
        {
            Visible = false
        };
        // Use CallDeferred to add child since we're in the _Ready phase
        GetTree().Root.CallDeferred("add_child", _modelLoadingWindow);

        // Subscribe to progress updates from AsyncModelLoader
        AsyncModelLoader.Instance.OnProgressUpdate += OnModelLoadingProgress;

        // Handle cancel button
        _modelLoadingWindow.OnCancel += OnModelLoadingCancelled;
    }

    /// <summary>
    /// Ensures the model loading window is created. Call this before showing the window.
    /// </summary>
    private void EnsureModelLoadingWindow()
    {
        if (_modelLoadingWindow == null)
        {
            InitializeModelLoadingWindow();
        }
    }

    private void OnModelLoadingProgress(float progress, string message)
    {
        // Call from main thread to update UI
        CallDeferred(nameof(UpdateModelLoadingProgress), progress, message);
    }

    private void UpdateModelLoadingProgress(float progress, string message)
    {
        if (_modelLoadingWindow != null && _modelLoadingWindow.Visible)
        {
            _modelLoadingWindow.UpdateProgress(progress, message);
        }
    }

    private void OnModelLoadingCancelled()
    {
        AsyncModelLoader.Instance.CancelLoading();
    }

    private void InitializeCategories()
    {
        _categories = new Dictionary<string, List<string>>()
        {
            {
                "Camera", new List<string>()
                {
                    "Camera"
                }
            },
            {
                "Light", new List<string>()
                {
                    "Point Light"
                }
            },
            {
                "Primitives", new List<string>()
                {
                    "Cube",
                    "Sphere",
                    "Cylinder",
                    "Cone",
                    "Torus",
                    "Plane",
                    "Capsule"
                }
            },
            {
                "Custom Models", new List<string>()
                {
                    "Load..."
                }
            }
        };

        // Load custom model history
        LoadCustomModelHistory();

        // Add Blocks category from loaded Minecraft models
        LoadMinecraftBlocks();

        // Add Items category from loaded Minecraft textures
        LoadMinecraftTextures();

        // Add Characters category from loaded GLB files
        LoadCharacters();
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
        }
    }

    private void LoadMinecraftTextures()
    {
        var textureLoader = MinecraftTextureLoader.Instance;
        if (!textureLoader.IsLoaded)
        {
            GD.PrintErr("Cannot load Minecraft textures - textures not loaded yet!");
            return;
        }

        var items = new List<string>();

        // Start with item textures by default
        foreach (var texturePath in textureLoader.GetAllItemTexturePaths())
        {
            // Extract the texture name from the path (e.g., "item/stone.png" -> "stone")
            var fileName = System.IO.Path.GetFileNameWithoutExtension(texturePath);

            // Skip .mcmeta files and other non-texture files
            if (!string.IsNullOrEmpty(fileName))
            {
                var displayName = CleanBlockName(fileName);
                if (!items.Contains(displayName))
                {
                    items.Add(displayName);
                }
            }
        }

        // Sort alphabetically for easier browsing
        items.Sort();

        if (items.Count > 0)
        {
            _categories["Items"] = items;
        }
    }

    private void LoadCharacters()
    {
        var characterLoader = CharacterLoader.Instance;
        if (!characterLoader.IsLoaded)
        {
            GD.PrintErr("Cannot load Characters - character files not loaded yet!");
            return;
        }

        var characters = characterLoader.GetAllCharacterNames();

        if (characters.Count > 0)
        {
            _categories["Characters"] = characters;
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

    private void OnTextureTypeSelected(long index)
    {
        _selectedTextureType = index == 0 ? "block" : "item";

        // Reload the Items category with the new texture type
        ReloadTextureItems();
    }

    private void ReloadTextureItems()
    {
        var textureLoader = MinecraftTextureLoader.Instance;
        if (!textureLoader.IsLoaded)
        {
            return;
        }

        var items = new List<string>();

        // Load textures based on selected type
        var texturePaths = _selectedTextureType == "block"
            ? textureLoader.GetAllBlockTexturePaths()
            : textureLoader.GetAllItemTexturePaths();

        foreach (var texturePath in texturePaths)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(texturePath);

            if (!string.IsNullOrEmpty(fileName))
            {
                var displayName = CleanBlockName(fileName);
                if (!items.Contains(displayName))
                {
                    items.Add(displayName);
                }
            }
        }

        items.Sort();
        _categories["Items"] = items;

        // Refresh the object list if Items category is selected
        if (_selectedCategory == "Items")
        {
            UpdateObjectList("Items");
        }
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

        // Show/hide the texture type dropdown and 3D plane checkbox based on category
        if (categoryName == "Items")
        {
            _textureTypeDropdown.Visible = true;
            _dropdownLabel.Visible = true;
            _spawn3DPlaneCheckbox.Visible = true;
        }
        else
        {
            _textureTypeDropdown.Visible = false;
            _dropdownLabel.Visible = false;
            _spawn3DPlaneCheckbox.Visible = false;
        }

        UpdateObjectList(categoryName);
    }

    private void UpdateObjectList(string categoryName)
    {
        _objectList.Clear();

        if (_categories.TryGetValue(categoryName, out List<string> value))
        {
            foreach (string objectName in value.Where(objectName => string.IsNullOrEmpty(_searchQuery) ||
                                                                    objectName.Contains(_searchQuery,
                                                                        System.StringComparison.OrdinalIgnoreCase)))
            {
                _objectList.AddItem(objectName);
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

        // Check if this is the "Load..." option in Custom Models
        if (_selectedCategory == "Custom Models")
        {
            var objectName = _objectList.GetItemText((int)index);
            if (objectName == "Load...")
            {
                _variantList.Clear();
                _spawnButton.Disabled = true;
                // Open file dialog on single-click
                OpenCustomModelFileDialog();
                return;
            }

            // It's a custom model from history, enable spawn button
            _variantList.Clear();
            _spawnButton.Disabled = false;
            return;
        }

        // Update variants list if this is a block
        if (_selectedCategory == "Blocks")
        {
            var objectName = _objectList.GetItemText((int)index);
            UpdateVariantList(objectName);
            // Don't set disabled here - let UpdateVariantList handle it
        }
        else if (_selectedCategory == "Items")
        {
            // For texture items, no variant selection needed
            _variantList.Clear();
            _spawnButton.Disabled = false;
        }
        else
        {
            // For primitives, no variant selection needed
            _variantList.Clear();
            _spawnButton.Disabled = false;
        }
    }

    private void OnObjectDoubleClicked(long index)
    {
        _selectedObjectIndex = (int)index;

        // Check if this is the "Load..." option in Custom Models
        if (_selectedCategory == "Custom Models")
        {
            var objectName = _objectList.GetItemText((int)index);
            if (objectName == "Load...")
            {
                OpenCustomModelFileDialog();
                return;
            }

            // It's a custom model from history, spawn it
            SpawnSelectedObject();
            return;
        }

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

        // Get variants for this blockstate
        var variants = MinecraftModelHelper.GetBlockStateVariants(fileName);

        if (variants != null && variants.Count > 0)
        {
            foreach (var displayName in variants.Select(variant => string.IsNullOrEmpty(variant) ? "Default" : variant))
            {
                _variantList.AddItem(displayName);
            }

            // Auto-select first variant and enable spawn button
            if (_variantList.ItemCount > 0)
            {
                _variantList.Select(0);
                _selectedVariantIndex = 0;
                _spawnButton.Disabled = false;
            }
        }
        else
        {
            _variantList.AddItem("(No variants found)");
            _spawnButton.Disabled = true;
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

        // Get the next available number for this object type
        int nextNumber = GetNextAvailableObjectNumber(objectName);
        string fullObjectName = nextNumber > 1 ? $"{objectName}{nextNumber}" : objectName;

        // Check if this is a character - needs special handling
        if (_selectedCategory == "Characters")
        {
            CreateCharacter(objectName, fullObjectName);
            return;
        }

        // Check if this is a custom model from history
        if (_selectedCategory == "Custom Models" && _customModelPaths.TryGetValue(objectName, out var modelPath))
        {
            _ = LoadAndSpawnCustomModel(modelPath);
            return;
        }

        // Create appropriate scene object (Camera, Light, or regular SceneObject)

        // Handle camera spawning specially
        if (objectName == "Camera")
        {
            var cameraObject = new CameraSceneObject();
            cameraObject.Name = fullObjectName;
            Viewport.AddChild(cameraObject);

            // Get the work camera and spawn the camera at its position and rotation
            var workCamera = Viewport.GetNodeOrNull<WorkCamera>("WorkCam");
            if (workCamera != null)
            {
                cameraObject.GlobalTransform = workCamera.GlobalTransform;
            }
            else
            {
                cameraObject.GlobalPosition = Vector3.Zero;
                GD.PrintErr("Could not find WorkCam - spawning at origin");
            }

            // Record undo command
            EditorCommandHistory.Instance?.PushWithoutExecute(new AddObjectCommand(cameraObject, Viewport));

            // Notify PreviewViewport about new camera
            if (GetTree().Root.GetNode<Main>("/root/Main") is Main main)
            {
                main.PreviewViewportControl?.OnCameraSpawned(cameraObject);
                main.SceneTreePanel.Refresh();
            }

            ApplyCameraMaterial(cameraObject);

            return;
        }

        // Handle light spawning specially
        if (objectName == "Point Light")
        {
            var lightObject = SpawnLightObject(fullObjectName);
            if (lightObject != null)
            {
                lightObject.SpawnCategory = "Light";
                // Record undo command
                EditorCommandHistory.Instance?.PushWithoutExecute(new AddObjectCommand(lightObject, Viewport));
                if (GetTree().Root.GetNode<Main>("/root/Main") is Main mainL)
                    mainL.SceneTreePanel.Refresh();
            }

            return;
        }

        var sceneObject = new SceneObject
        {
            Name = fullObjectName,
            ObjectType = objectName
        };

        Node3D visualNode = null;

        switch (_selectedCategory)
        {
            // Check if this is a Minecraft block
            case "Blocks":
            {
                visualNode = CreateMinecraftBlock(objectName);
                // Minecraft blocks are already positioned correctly in their models
                sceneObject.PivotOffset = new Vector3(0.5f, 0, 0.5f);
                sceneObject.SpawnCategory = "Blocks";
                // Save the variant so it can be restored
                if (_selectedVariantIndex >= 0 && _selectedVariantIndex < _variantList.ItemCount)
                {
                    var variantName = _variantList.GetItemText(_selectedVariantIndex);
                    sceneObject.BlockVariant = variantName == "Default" ? "" : variantName;
                }

                break;
            }
            case "Items":
            {
                // Create texture plane
                visualNode = CreateTexturePlane(objectName);
                // Texture planes are centered
                sceneObject.PivotOffset = new Vector3(0.5f, 0, 0.03125f);
                if (visualNode != null)
                    visualNode.Position = new Vector3(0.5f, 0.5f, -0.03125f);
                sceneObject.SpawnCategory = "Items";
                sceneObject.TextureType = _selectedTextureType;
                break;
            }
            default:
            {
                // Create primitive mesh
                var meshInstance = new MeshInstance3D();
                var mesh = CreatePrimitiveMesh(objectName);

                if (mesh != null)
                {
                    meshInstance.Mesh = mesh;

                    // Create a material for the mesh
                    var material = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.8f, 0.8f, 0.8f)
                    };

                    // Apply material to mesh surface instead of using MaterialOverride
                    if (mesh is PrimitiveMesh primitiveMesh)
                    {
                        primitiveMesh.Material = material;
                    }

                    // Set shadows
                    meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

                    visualNode = meshInstance;
                }

                // Set pivot offset for primitives so they sit on the ground
                sceneObject.PivotOffset = new Vector3(0, -0.5f, 0);
                sceneObject.SpawnCategory = "Primitives";
                break;
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

            // Record undo command
            EditorCommandHistory.Instance?.PushWithoutExecute(new AddObjectCommand(sceneObject, Viewport));

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

    /// <summary>
    /// Creates a LightSceneObject and adds it to the viewport.
    /// Returns the created object, or null on failure.
    /// </summary>
    public LightSceneObject SpawnLightObject(string objectName)
    {
        if (Viewport == null) return null;
        var lightObject = new LightSceneObject();
        lightObject.Name = objectName;
        Viewport.AddChild(lightObject);
        lightObject.GlobalPosition = Vector3.Zero;
        return lightObject;
    }

    /// <summary>Creates a CameraSceneObject and adds it to the viewport.</summary>
    public CameraSceneObject SpawnCameraObject(string objectName)
    {
        if (Viewport == null) return null;
        var cameraObject = new CameraSceneObject();
        cameraObject.Name = objectName;
        Viewport.AddChild(cameraObject);
        cameraObject.GlobalPosition = Vector3.Zero;

        ApplyCameraMaterial(cameraObject);

        return cameraObject;
    }

    /// <summary>
    /// Applies CameraMaterial.tres to all mesh instances of a CameraSceneObject.
    /// </summary>
    private void ApplyCameraMaterial(CameraSceneObject cameraObject)
    {
        if (cameraObject == null) return;

        var material = GD.Load<StandardMaterial3D>("res://assets/mesh/CameraMaterial.tres");
        if (material == null)
        {
            GD.PrintErr("ApplyCameraMaterial: failed to load CameraMaterial.tres");
            return;
        }

        var meshInstances = cameraObject.GetMeshInstancesRecursively(cameraObject.Visual);
        foreach (var meshInstance in meshInstances)
        {
            if (meshInstance.Mesh == null) continue;

            for (int i = 0; i < meshInstance.Mesh.GetSurfaceCount(); i++)
            {
                meshInstance.Mesh.SurfaceSetMaterial(i, material);
            }
        }
    }

    /// <summary>
    /// Creates a Minecraft block SceneObject and adds it to the viewport.
    /// <paramref name="blockName"/> is the display name (e.g. "Grass Block").
    /// <paramref name="variant"/> is the blockstate variant string (empty = default).
    /// Returns the created object, or null on failure.
    /// </summary>
    public SceneObject SpawnBlockObject(string blockName, string variant, string objectName)
    {
        if (Viewport == null) return null;

        var fileName = blockName.ToLower().Replace(" ", "_");
        var blockNode = MinecraftModelHelper.CreateNodeFromBlockState(fileName, variant);
        if (blockNode == null)
        {
            GD.PrintErr($"SpawnMenu.SpawnBlockObject: failed to create block '{blockName}'");
            return null;
        }

        var sceneObject = new SceneObject
        {
            Name = objectName,
            ObjectType = blockName,
            SpawnCategory = "Blocks",
            BlockVariant = variant,
        };
        sceneObject.PivotOffset = new Vector3(0.5f, 0, 0.5f);
        sceneObject.AddVisualInstance(blockNode);
        Viewport.AddChild(sceneObject);
        sceneObject.GlobalPosition = Vector3.Zero;
        return sceneObject;
    }

    /// <summary>
    /// Creates a Minecraft item/block texture plane SceneObject and adds it to the viewport.
    /// <paramref name="itemName"/> is the display name (e.g. "Diamond Sword").
    /// <paramref name="textureType"/> is "block" or "item".
    /// Returns the created object, or null on failure.
    /// </summary>
    public SceneObject SpawnItemObject(string itemName, string textureType, string objectName)
    {
        if (Viewport == null) return null;

        // Temporarily override the texture type so CreateTexturePlane uses the right one
        var savedType = _selectedTextureType;
        _selectedTextureType = textureType;
        var visualNode = CreateTexturePlane(itemName);
        _selectedTextureType = savedType;

        if (visualNode == null)
        {
            GD.PrintErr($"SpawnMenu.SpawnItemObject: failed to create texture plane for '{itemName}'");
            return null;
        }

        var sceneObject = new SceneObject
        {
            Name = objectName,
            ObjectType = itemName,
            SpawnCategory = "Items",
            TextureType = textureType,
        };
        sceneObject.PivotOffset = new Vector3(0.5f, 0, 0.03125f);
        visualNode.Position = new Vector3(0.5f, 0.5f, -0.03125f);
        sceneObject.AddVisualInstance(visualNode);
        Viewport.AddChild(sceneObject);
        sceneObject.GlobalPosition = Vector3.Zero;
        return sceneObject;
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
            return blockNode;
        }

        GD.PrintErr($"Failed to create Minecraft block: {blockName}");
        return null;
    }

    private int GetNextAvailableObjectNumber(string objectType)
    {
        var existingNumbers = new HashSet<int>();

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
    }

    /// <summary>
    /// Creates a primitive SceneObject and adds it to the viewport.
    /// Returns the created SceneObject, or null on failure.
    /// Used by the project restore system.
    /// </summary>
    public SceneObject SpawnPrimitiveObject(string primitiveType, string objectName)
    {
        if (Viewport == null)
        {
            GD.PrintErr("SpawnMenu.SpawnPrimitiveObject: Viewport not set");
            return null;
        }

        var mesh = CreatePrimitiveMesh(primitiveType);
        if (mesh == null)
        {
            GD.PrintErr($"SpawnMenu.SpawnPrimitiveObject: unknown primitive type '{primitiveType}'");
            return null;
        }

        var sceneObject = new SceneObject
        {
            Name = objectName,
            ObjectType = primitiveType,
        };

        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = mesh;

        var material = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.8f, 0.8f) };
        if (mesh is PrimitiveMesh primitiveMesh)
            primitiveMesh.Material = material;

        meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        sceneObject.PivotOffset = new Vector3(0, -0.5f, 0);
        sceneObject.AddVisualInstance(meshInstance);
        Viewport.AddChild(sceneObject);
        sceneObject.GlobalPosition = Vector3.Zero;

        return sceneObject;
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

    private Node3D CreateTexturePlane(string itemName)
    {
        // Convert display name back to file name format
        var fileName = itemName.ToLower().Replace(" ", "_");

        bool create3DPlane = _spawn3DPlaneCheckbox.ButtonPressed;

        // Get the texture from the texture loader
        var textureLoader = MinecraftTextureLoader.Instance;
        ImageTexture texture = _selectedTextureType == "block"
            ? textureLoader.GetBlockTexture(fileName)
            : textureLoader.GetItemTexture(fileName);

        if (texture == null)
        {
            GD.PrintErr($"Failed to load texture: {fileName}");
            return null;
        }

        // Get texture dimensions to scale the plane appropriately
        var image = texture.GetImage();
        float aspectRatio = (float)image.GetWidth() / image.GetHeight();

        // Calculate size ensuring it doesn't exceed 1 meter on XY axis
        Vector2 planeSize;
        if (aspectRatio > 1)
        {
            // Width is larger
            planeSize = new Vector2(Mathf.Min(aspectRatio, 1.0f), Mathf.Min(1.0f / aspectRatio, 1.0f));
        }
        else
        {
            // Height is larger
            planeSize = new Vector2(Mathf.Min(aspectRatio, 1.0f), Mathf.Min(1.0f / aspectRatio, 1.0f));
        }

        Node3D resultNode;

        if (create3DPlane)
        {
            // Create a 3D extruded plane using a BoxMesh
            resultNode = Create3DExtrudedPlane(texture, planeSize);
        }
        else
        {
            // Create a simple 2D plane
            var meshInstance = new MeshInstance3D();
            var planeMesh = new PlaneMesh
            {
                Size = planeSize
            };
            meshInstance.Mesh = planeMesh;

            // Rotate the plane to make it vertical (90 degrees around X axis)
            meshInstance.RotationDegrees = new Vector3(90, 0, 0);

            // Create a material with the texture
            var material = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, // Pixelated look for Minecraft textures
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, // Handle transparency
                AlphaScissorThreshold = 0.5f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled // Show both sides of the plane
            };

            // Apply material to mesh surface instead of using MaterialOverride
            planeMesh.Material = material;
            meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

            resultNode = meshInstance;
        }

        return resultNode;
    }

    private Node3D Create3DExtrudedPlane(ImageTexture texture, Vector2 planeSize)
    {
        // Create an extruded mesh from the pixel data as a vertical plane
        const float thickness = 0.0625f;

        var image = texture.GetImage();
        int width = image.GetWidth();
        int height = image.GetHeight();

        // Calculate scale to fit within planeSize while maintaining aspect ratio
        float scale = Mathf.Min(planeSize.X / width, planeSize.Y / height);

        // Create arrays for mesh data
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        // Process each pixel to create extruded quads
        // For a vertical plane, X maps to horizontal, Y maps to vertical, Z is depth
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var color = image.GetPixel(x, y);

                // Only create geometry for non-transparent pixels
                if (color.A > 0.5f)
                {
                    // Calculate position for vertical plane
                    // X: horizontal position (centered)
                    // Y: vertical position (centered, inverted because tex coords are top-down)
                    // Z: depth (extrusion direction)
                    float px = (x - width / 2.0f) * scale;
                    float py = -(y - height / 2.0f) * scale; // Negative to flip Y
                    float halfThickness = thickness / 2.0f;

                    // UV coordinates for this pixel
                    float uvX = (x + 0.5f) / width;
                    float uvY = (y + 0.5f) / height;

                    int baseVertex = vertices.Count;

                    // Create a box for this pixel (6 faces)
                    // Front face (facing +Z, towards camera) - CCW winding: bottom-left, bottom-right, top-right, top-left
                    AddQuad(vertices, normals, uvs, indices, baseVertex,
                        new Vector3(px - scale / 2, py - scale / 2, halfThickness),
                        new Vector3(px + scale / 2, py - scale / 2, halfThickness),
                        new Vector3(px + scale / 2, py + scale / 2, halfThickness),
                        new Vector3(px - scale / 2, py + scale / 2, halfThickness),
                        new Vector3(0, 0, 1), uvX, uvY); // Normal pointing outward (positive Z)

                    baseVertex = vertices.Count;
                    // Back face (facing -Z, away from camera) - CCW winding when viewed from outside
                    AddQuad(vertices, normals, uvs, indices, baseVertex,
                        new Vector3(px + scale / 2, py - scale / 2, -halfThickness),
                        new Vector3(px - scale / 2, py - scale / 2, -halfThickness),
                        new Vector3(px - scale / 2, py + scale / 2, -halfThickness),
                        new Vector3(px + scale / 2, py + scale / 2, -halfThickness),
                        new Vector3(0, 0, -1), uvX, uvY); // Normal pointing outward (negative Z)

                    // Only add side faces if adjacent pixels are transparent (edge pixels)
                    bool leftEmpty = x == 0 || image.GetPixel(x - 1, y).A <= 0.5f;
                    bool rightEmpty = x == width - 1 || image.GetPixel(x + 1, y).A <= 0.5f;
                    bool topEmpty = y == 0 || image.GetPixel(x, y - 1).A <= 0.5f;
                    bool bottomEmpty = y == height - 1 || image.GetPixel(x, y + 1).A <= 0.5f;

                    if (leftEmpty)
                    {
                        baseVertex = vertices.Count;
                        AddQuad(vertices, normals, uvs, indices, baseVertex,
                            new Vector3(px - scale / 2, py - scale / 2, -halfThickness),
                            new Vector3(px - scale / 2, py - scale / 2, halfThickness),
                            new Vector3(px - scale / 2, py + scale / 2, halfThickness),
                            new Vector3(px - scale / 2, py + scale / 2, -halfThickness),
                            Vector3.Left, uvX, uvY);
                    }

                    if (rightEmpty)
                    {
                        baseVertex = vertices.Count;
                        AddQuad(vertices, normals, uvs, indices, baseVertex,
                            new Vector3(px + scale / 2, py - scale / 2, halfThickness),
                            new Vector3(px + scale / 2, py - scale / 2, -halfThickness),
                            new Vector3(px + scale / 2, py + scale / 2, -halfThickness),
                            new Vector3(px + scale / 2, py + scale / 2, halfThickness),
                            Vector3.Right, uvX, uvY);
                    }

                    if (topEmpty)
                    {
                        baseVertex = vertices.Count;
                        AddQuad(vertices, normals, uvs, indices, baseVertex,
                            new Vector3(px - scale / 2, py + scale / 2, -halfThickness),
                            new Vector3(px - scale / 2, py + scale / 2, halfThickness),
                            new Vector3(px + scale / 2, py + scale / 2, halfThickness),
                            new Vector3(px + scale / 2, py + scale / 2, -halfThickness),
                            Vector3.Up, uvX, uvY);
                    }

                    if (bottomEmpty)
                    {
                        baseVertex = vertices.Count;
                        AddQuad(vertices, normals, uvs, indices, baseVertex,
                            new Vector3(px - scale / 2, py - scale / 2, -halfThickness),
                            new Vector3(px + scale / 2, py - scale / 2, -halfThickness),
                            new Vector3(px + scale / 2, py - scale / 2, halfThickness),
                            new Vector3(px - scale / 2, py - scale / 2, halfThickness),
                            Vector3.Down, uvX, uvY);
                    }
                }
            }
        }

        // Create the mesh
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var meshInstance = new MeshInstance3D
        {
            Mesh = arrayMesh
        };

        // No rotation needed - mesh is already vertical

        // Create a material with the texture
        var material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Back
        };

        // Apply material to mesh surface instead of using MaterialOverride
        if (arrayMesh.GetSurfaceCount() > 0)
        {
            arrayMesh.SurfaceSetMaterial(0, material);
        }

        meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

        return meshInstance;
    }

    private void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
        List<int> indices, int baseVertex, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
        Vector3 normal, float uvX, float uvY)
    {
        // Add vertices
        vertices.Add(v0);
        vertices.Add(v1);
        vertices.Add(v2);
        vertices.Add(v3);

        // Add normals
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        // Add UVs (use the pixel's texture coordinate)
        uvs.Add(new Vector2(uvX, uvY));
        uvs.Add(new Vector2(uvX, uvY));
        uvs.Add(new Vector2(uvX, uvY));
        uvs.Add(new Vector2(uvX, uvY));

        // Add indices for two triangles with correct winding order (counter-clockwise)
        indices.Add(baseVertex + 0);
        indices.Add(baseVertex + 2);
        indices.Add(baseVertex + 1);

        indices.Add(baseVertex + 0);
        indices.Add(baseVertex + 3);
        indices.Add(baseVertex + 2);
    }

    public void ShowMenu(Vector2 position)
    {
        // Set popup position and size
        var rect = new Rect2I((Vector2I)position, new Vector2I(900, 400));
        PopupOnParent(rect);
    }

    private async void CreateCharacter(string characterName, string fullObjectName)
    {
        // Get the character GLB path
        var characterLoader = CharacterLoader.Instance;
        var glbPath = characterLoader.GetCharacterPath(characterName);

        if (string.IsNullOrEmpty(glbPath))
        {
            GD.PrintErr($"Could not find GLB file for character: {characterName}");
            return;
        }

        if (!System.IO.File.Exists(glbPath))
        {
            GD.PrintErr($"GLB file does not exist: {glbPath}");
            return;
        }

        // Show progress window for model loading
        _modelLoadingWindow.SetModelName(System.IO.Path.GetFileNameWithoutExtension(glbPath));
        _modelLoadingWindow.ShowWindow();

        // Use async model loader to properly handle texture preloading and shader compilation
        var glbRoot3D = await AsyncModelLoader.Instance.LoadGlbAsync(glbPath);

        // Hide progress window
        _modelLoadingWindow.HideWindow();

        if (glbRoot3D == null)
        {
            GD.PrintErr("Failed to load GLB file asynchronously");
            return;
        }

        // Create a CharacterSceneObject
        var characterObject = new CharacterSceneObject
        {
            Name = fullObjectName,
            ObjectType = "Character", // Use "Character" as the object type for all characters
            SpawnCategory = "Character", // Mark as Character for proper save/restore
        };
        // Store the actual character name (e.g., "Steve") so we can restore it
        characterObject.CharacterName = characterName;

        // Add to viewport first
        Viewport.AddChild(characterObject);

        // Setup the character from the GLB data
        characterObject.SetupFromGlb(glbRoot3D);

        // Model is now fully loaded with textures preloaded and shaders compiled
        // Position at world origin
        characterObject.GlobalPosition = Vector3.Zero;

        // Record undo command
        EditorCommandHistory.Instance?.PushWithoutExecute(new AddObjectCommand(characterObject, Viewport));

        // Notify the scene tree panel to refresh
        if (GetTree().Root.GetNode<Main>("/root/Main") is Main main)
        {
            main.SceneTreePanel.Refresh();
        }
    }

    private void OpenCustomModelFileDialog()
    {
        NativeFileDialog.ShowOpenFile(
            "Select 3D Model (GLB/GLTF/Mine Imator/.blend)",
            NativeFileDialog.Filters.Models,
            (success, filePath) =>
            {
                if (success && !string.IsNullOrEmpty(filePath))
                {
                    _ = LoadAndSpawnCustomModel(filePath);
                }
            }
        );
    }

    private async System.Threading.Tasks.Task LoadAndSpawnCustomModel(string modelPath)
    {
        if (!System.IO.File.Exists(modelPath))
        {
            GD.PrintErr($"Model file does not exist: {modelPath}");
            return;
        }

        // Get a display name from the file
        var fileName = System.IO.Path.GetFileNameWithoutExtension(modelPath);
        var extension = System.IO.Path.GetExtension(modelPath).ToLower();
        var displayName = CleanBlockName(fileName);

        // Get the next available number for this object type
        int nextNumber = GetNextAvailableObjectNumber(displayName);
        string fullObjectName = nextNumber > 1 ? $"{displayName}{nextNumber}" : displayName;

        // Ensure progress window is initialized and show it
        EnsureModelLoadingWindow();
        bool progressWindowAvailable = _modelLoadingWindow != null;
        if (progressWindowAvailable)
        {
            _modelLoadingWindow.SetModelName(displayName);
            _modelLoadingWindow.ShowWindow();
            _modelLoadingWindow.UpdateProgress(0.1f, $"Loading {extension.ToUpper()} file...");
        }

        SceneObject customModelObject = null;

        // Check if this is a Mine Imator model
        if (extension == ".mimodel")
        {
            if (progressWindowAvailable)
                _modelLoadingWindow.UpdateProgress(0.3f, "Loading Mine Imator model...");

            // Load Mine Imator model - it returns a CharacterSceneObject directly
            var character = LoadMineImatorModel(modelPath);

            if (progressWindowAvailable)
                _modelLoadingWindow.UpdateProgress(0.7f, "Creating character object...");

            if (character != null)
            {
                character.Name = fullObjectName;
                character.ObjectType = displayName;
                Viewport.AddChild(character);
                customModelObject = character;
            }
        }
        else if (extension == ".miobject")
        {
            if (progressWindowAvailable)
                _modelLoadingWindow.UpdateProgress(0.3f, "Loading Mine Imator object...");

            // Load Mine Imator object - contains a scene with multiple models
            var sceneRoot = LoadMiObjectScene(modelPath);

            if (progressWindowAvailable)
                _modelLoadingWindow.UpdateProgress(0.7f, "Creating scene object...");

            if (sceneRoot != null)
            {
                sceneRoot.Name = fullObjectName;
                sceneRoot.ObjectType = displayName;
                Viewport.AddChild(sceneRoot);
                customModelObject = sceneRoot;
            }
        }
        else
        {
            // Load as GLB/GLTF using async loader for proper texture preloading
            // and shader compilation waiting

            if (progressWindowAvailable)
                _modelLoadingWindow.UpdateProgress(0.3f, "Loading GLB/GLTF file...");

            var modelRoot = await AsyncModelLoader.Instance.LoadGlbAsync(modelPath);

            if (progressWindowAvailable)
                _modelLoadingWindow.UpdateProgress(0.8f, "Processing model...");

            if (modelRoot == null)
            {
                if (progressWindowAvailable)
                    _modelLoadingWindow.HideWindow();
                GD.PrintErr($"Failed to load model: {modelPath}");
                return;
            }

            // Check if the model has a skeleton
            bool hasSkeleton = HasSkeleton(modelRoot);

            if (hasSkeleton)
            {
                // Create a CharacterSceneObject for rigged models
                var characterObject = new CharacterSceneObject
                {
                    Name = fullObjectName,
                    ObjectType = displayName
                };

                // Add to viewport first
                Viewport.AddChild(characterObject);

                // Setup the character from the GLB data
                characterObject.SetupFromGlb(modelRoot);

                customModelObject = characterObject;
            }
            else
            {
                // Create a regular SceneObject for static models
                customModelObject = new SceneObject
                {
                    Name = fullObjectName,
                    ObjectType = displayName,
                    PivotOffset = Vector3.Zero
                };

                // Add to viewport first
                Viewport.AddChild(customModelObject);

                // Add the model root directly to the visual node
                customModelObject.AddVisualInstance(modelRoot);
            }
        }

        if (customModelObject == null)
        {
            GD.PrintErr($"Failed to create scene object from model: {modelPath}");
            return;
        }

        // Record the source asset path so the project save system can restore it
        customModelObject.SourceAssetPath = modelPath;

        // Model is now fully loaded with textures preloaded and shaders compiled

        // Position at world origin
        customModelObject.GlobalPosition = Vector3.Zero;

        // Record undo command
        EditorCommandHistory.Instance?.PushWithoutExecute(new AddObjectCommand(customModelObject, Viewport));

        // Notify the scene tree panel to refresh
        if (GetTree().Root.GetNode<Main>("/root/Main") is { } main)
        {
            main.SceneTreePanel.Refresh();
        }

        // Auto-import the model into the current project (if one is open),
        // but only if the file is NOT already inside the project folder
        // (avoids duplicating assets that were spawned from the Content Drawer).
        if (!string.IsNullOrEmpty(ProjectManager.CurrentProjectFolder))
        {
            var projectFolder = ProjectManager.CurrentProjectFolder
                .TrimEnd(System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar);
            var normalizedModel = System.IO.Path.GetFullPath(modelPath);
            var normalizedProject = System.IO.Path.GetFullPath(projectFolder);

            bool alreadyInProject = normalizedModel.StartsWith(
                normalizedProject + System.IO.Path.DirectorySeparatorChar,
                System.StringComparison.OrdinalIgnoreCase);

            if (!alreadyInProject)
            {
                // Import the asset into the project folder and update SourceAssetPath
                // to point to the imported copy.  Without this update the save system
                // would record the original external path, and on the next project open
                // the alreadyInProject check would fail and create a duplicate copy.
                var importedPath = ProjectManager.ImportAsset(modelPath);
                if (!string.IsNullOrEmpty(importedPath))
                    customModelObject.SourceAssetPath = importedPath;
            }
        }

        // Add to history using the final resolved path (imported copy if applicable)
        AddToCustomModelHistory(customModelObject.SourceAssetPath, displayName);

        // Hide the progress window when done
        if (progressWindowAvailable)
        {
            _modelLoadingWindow.UpdateProgress(1.0f, "Complete!");
            _modelLoadingWindow.HideWindow();
        }

        // Hide the menu after spawning
        Hide();
    }

    /// <summary>
    /// Fire-and-forget entry point (used by the Content Drawer single-click spawn).
    /// </summary>
    public void SpawnModelFromPath(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
        {
            GD.PrintErr($"SpawnMenu.SpawnModelFromPath: file not found '{modelPath}'");
            return;
        }

        // Discard the task intentionally — caller does not need to await
        _ = LoadAndSpawnCustomModel(modelPath);
    }

    /// <summary>
    /// Awaitable entry point used by the project restore system so it can
    /// show a progress bar and wait for each model to finish loading.
    /// </summary>
    public System.Threading.Tasks.Task SpawnModelFromPathAsync(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
        {
            GD.PrintErr($"SpawnMenu.SpawnModelFromPathAsync: file not found '{modelPath}'");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        return LoadAndSpawnCustomModel(modelPath);
    }

    /// <summary>
    /// Loads a GLB/GLTF model file
    /// </summary>
    private Node3D LoadGlbModel(string glbPath)
    {
        // Load the GLB file using Godot's gltf_document and gltf_state
        var gltfDocument = new GltfDocument();
        var gltfState = new GltfState();

        var error = gltfDocument.AppendFromFile(glbPath, gltfState);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to load GLB file: {error}");
            return null;
        }

        // Generate the scene from GLTF
        var glbRoot = gltfDocument.GenerateScene(gltfState);

        if (glbRoot == null)
        {
            GD.PrintErr("Failed to generate scene from GLB");
            return null;
        }

        // Cast to Node3D - GLB/GLTF files typically contain 3D content
        if (glbRoot is not Node3D glbRoot3D)
        {
            GD.PrintErr($"GLB root is not a Node3D, it's a {glbRoot.GetType().Name}");
            glbRoot.QueueFree();
            return null;
        }

        return glbRoot3D;
    }

    /// <summary>
    /// Loads a Mine Imator model file (.mimodel)
    /// </summary>
    private CharacterSceneObject LoadMineImatorModel(string mimodelPath)
    {
        var loader = MineImatorLoader.Instance;
        var model = loader.LoadModel(mimodelPath);

        if (model == null)
        {
            GD.PrintErr($"Failed to load Mine Imator model: {mimodelPath}");
            return null;
        }

        // Create a CharacterSceneObject with bones from the model
        var character = loader.CreateCharacterFromModel(model);

        if (character == null)
        {
            GD.PrintErr($"Failed to create character from Mine Imator model: {mimodelPath}");
            return null;
        }

        return character;
    }

    /// <summary>
    /// Loads a Mine Imator object file (.miobject) which contains a scene with multiple models
    /// </summary>
    private SceneObject LoadMiObjectScene(string miobjectPath)
    {
        var loader = MineImatorLoader.Instance;
        var miObject = loader.LoadMiObject(miobjectPath);

        if (miObject == null)
        {
            GD.PrintErr($"Failed to load Mine Imator object: {miobjectPath}");
            return null;
        }

        // Create a scene from the .miobject
        var sceneRoot = loader.CreateSceneFromMiObject(miObject);

        if (sceneRoot == null)
        {
            GD.PrintErr($"Failed to create scene from Mine Imator object: {miobjectPath}");
            return null;
        }

        return sceneRoot;
    }

    /// <summary>
    /// Checks if a Node3D hierarchy contains a Skeleton3D
    /// </summary>
    private bool HasSkeleton(Node node)
    {
        if (node is Skeleton3D)
        {
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (HasSkeleton(child))
            {
                return true;
            }
        }

        return false;
    }

    private void LoadCustomModelHistory()
    {
        // History is now in-memory only - will be set per-project when project system is implemented
        // Update the Custom Models category list
        UpdateCustomModelsCategory();
    }

    private void SaveCustomModelHistory()
    {
        // History is now in-memory only - will be saved per-project when project system is implemented
    }

    private void AddToCustomModelHistory(string glbPath, string displayName)
    {
        // Remove if already in history (we'll re-add at the top)
        if (_customModelHistory != null && _customModelHistory.Contains(glbPath))
        {
            _customModelHistory.Remove(glbPath);
            // Remove old display name entry
            var oldKey = _customModelPaths.FirstOrDefault(x => x.Value == glbPath).Key;
            if (!string.IsNullOrEmpty(oldKey))
            {
                _customModelPaths.Remove(oldKey);
            }
        }

        // Add to the beginning of the list (most recent first)
        _customModelHistory?.Insert(0, glbPath);
        _customModelPaths[displayName] = glbPath;

        // Update the category list
        UpdateCustomModelsCategory();

        // Refresh the object list if Custom Models is the selected category
        if (_selectedCategory == "Custom Models")
        {
            UpdateObjectList("Custom Models");
        }
    }

    private void UpdateCustomModelsCategory()
    {
        var customModelsList = new List<string> { "Load..." };
        customModelsList.AddRange(_customModelPaths.Select(kvp => kvp.Key));

        // Add all items from history
        _categories["Custom Models"] = customModelsList;
    }
}