using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Manages the project save/load system.
///
/// Project folder structure:
///   MyProject/
///     MyProject.srproject   ← main project file (JSON)
///     assets/               ← user-imported assets (models, images, audio, etc.)
///       models/
///       images/
///       audio/
///     renders/              ← output renders
///
/// The .srproject file stores all scene objects, their transforms, keyframes,
/// project settings (resolution, framerate, background, etc.) and a manifest
/// of every asset file that has been imported into the project.
/// </summary>
public static class ProjectManager
{
	// ── Public state ────────────────────────────────────────────────────────

	/// <summary>Full path to the currently open project folder, or empty string.</summary>
	public static string CurrentProjectFolder { get; private set; } = "";

	/// <summary>Full path to the main .srproject file, or empty string.</summary>
	public static string CurrentProjectFile { get; private set; } = "";

	/// <summary>Display name of the current project (derived from folder name).</summary>
	public static string CurrentProjectName { get; private set; } = "Untitled";

	/// <summary>True when the project has unsaved changes.</summary>
	public static bool IsDirty { get; private set; } = false;

	// ── Events ───────────────────────────────────────────────────────────────

	/// <summary>Fired after a project is successfully created or opened.</summary>
	public static event Action<string> ProjectOpened;

	/// <summary>Fired after a project is successfully saved.</summary>
	public static event Action ProjectSaved;

	/// <summary>Fired when the project is closed / reset to a new blank state.</summary>
	public static event Action ProjectClosed;

	/// <summary>Fired when the dirty (unsaved-changes) state changes.</summary>
	public static event Action<bool> DirtyChanged;

	/// <summary>Fired when the asset manifest changes (import / remove).</summary>
	public static event Action AssetsChanged;

	// ── Internal ─────────────────────────────────────────────────────────────

	private static ProjectData _currentData = new ProjectData();

	// ── Recent projects ───────────────────────────────────────────────────────

	/// <summary>Maximum number of recent project entries to keep.</summary>
	private const int MaxRecentProjects = 10;

	/// <summary>File name for the recent projects list stored in user data.</summary>
	private const string RecentProjectsFileName = "recent_projects.json";

	/// <summary>File name for the user preferences stored in user data.</summary>
	private const string UserPrefsFileName = "user_prefs.json";

	/// <summary>
	/// Returns the folder path that was last used when creating a new project,
	/// or an empty string if none has been saved yet.
	/// </summary>
	public static string GetLastNewProjectFolder()
	{
		try
		{
			var path = Path.Combine(OS.GetUserDataDir(), UserPrefsFileName);
			if (!File.Exists(path)) return "";
			var json = File.ReadAllText(path);
			var prefs = JsonSerializer.Deserialize<UserPrefs>(json, JsonOptions);
			return prefs?.LastNewProjectFolder ?? "";
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.GetLastNewProjectFolder failed: {ex.Message}");
			return "";
		}
	}

	/// <summary>
	/// Saves <paramref name="folderPath"/> as the default folder for new project creation.
	/// </summary>
	public static void SaveLastNewProjectFolder(string folderPath)
	{
		if (string.IsNullOrEmpty(folderPath)) return;
		try
		{
			var path = Path.Combine(OS.GetUserDataDir(), UserPrefsFileName);
			UserPrefs prefs;
			if (File.Exists(path))
			{
				var existing = File.ReadAllText(path);
				prefs = JsonSerializer.Deserialize<UserPrefs>(existing, JsonOptions) ?? new UserPrefs();
			}
			else
			{
				prefs = new UserPrefs();
			}
			prefs.LastNewProjectFolder = folderPath;
			File.WriteAllText(path, JsonSerializer.Serialize(prefs, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.SaveLastNewProjectFolder failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Returns the list of recently opened projects, most recent first.
	/// Each entry is a <see cref="RecentProjectEntry"/>.
	/// </summary>
	public static IReadOnlyList<RecentProjectEntry> GetRecentProjects()
	{
		return LoadRecentProjects().AsReadOnly();
	}

	/// <summary>
	/// Adds or updates a recent project entry for the given project file path.
	/// Moves it to the top of the list if it already exists.
	/// </summary>
	private static void AddToRecentProjects(string projectFilePath, string projectName)
	{
		if (string.IsNullOrEmpty(projectFilePath)) return;

		var list = LoadRecentProjects();

		// Remove any existing entry for this path
		list.RemoveAll(e => string.Equals(e.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase));

		// Insert at the front
		list.Insert(0, new RecentProjectEntry
		{
			FilePath    = projectFilePath,
			ProjectName = projectName,
			LastOpened  = DateTime.UtcNow.ToString("o"),
		});

		// Trim to max
		if (list.Count > MaxRecentProjects)
			list.RemoveRange(MaxRecentProjects, list.Count - MaxRecentProjects);

		SaveRecentProjects(list);
	}

	/// <summary>
	/// Removes a recent project entry by file path (e.g. when the file no longer exists).
	/// </summary>
	public static void RemoveFromRecentProjects(string projectFilePath)
	{
		if (string.IsNullOrEmpty(projectFilePath)) return;
		var list = LoadRecentProjects();
		list.RemoveAll(e => string.Equals(e.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase));
		SaveRecentProjects(list);
	}

	private static List<RecentProjectEntry> LoadRecentProjects()
	{
		try
		{
			var path = Path.Combine(OS.GetUserDataDir(), RecentProjectsFileName);
			if (!File.Exists(path)) return new List<RecentProjectEntry>();
			var json = File.ReadAllText(path);
			return JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, JsonOptions)
			       ?? new List<RecentProjectEntry>();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.LoadRecentProjects failed: {ex.Message}");
			return new List<RecentProjectEntry>();
		}
	}

	private static void SaveRecentProjects(List<RecentProjectEntry> list)
	{
		try
		{
			var path = Path.Combine(OS.GetUserDataDir(), RecentProjectsFileName);
			var json = JsonSerializer.Serialize(list, JsonOptions);
			File.WriteAllText(path, json);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.SaveRecentProjects failed: {ex.Message}");
		}
	}

	// ── Sub-folder names ─────────────────────────────────────────────────────

	public const string AssetsFolderName  = "assets";
	public const string ModelsFolderName  = "models";
	public const string ImagesFolderName  = "images";
	public const string AudioFolderName   = "audio";
	public const string RendersFolderName = "renders";

	// ── Derived paths ─────────────────────────────────────────────────────────

	public static string AssetsFolder  => Path.Combine(CurrentProjectFolder, AssetsFolderName);
	public static string ModelsFolder  => Path.Combine(AssetsFolder, ModelsFolderName);
	public static string ImagesFolder  => Path.Combine(AssetsFolder, ImagesFolderName);
	public static string AudioFolder   => Path.Combine(AssetsFolder, AudioFolderName);
	public static string RendersFolder => Path.Combine(CurrentProjectFolder, RendersFolderName);

	// ── Project lifecycle ─────────────────────────────────────────────────────

	/// <summary>
	/// Creates a new blank project at the given folder path.
	/// The folder must not already contain a .srproject file.
	/// </summary>
	public static bool NewProject(string projectFolder)
	{
		if (string.IsNullOrWhiteSpace(projectFolder))
		{
			GD.PrintErr("ProjectManager.NewProject: projectFolder is empty");
			return false;
		}

		try
		{
			// Create the folder structure
			Directory.CreateDirectory(projectFolder);
			Directory.CreateDirectory(Path.Combine(projectFolder, AssetsFolderName, ModelsFolderName));
			Directory.CreateDirectory(Path.Combine(projectFolder, AssetsFolderName, ImagesFolderName));
			Directory.CreateDirectory(Path.Combine(projectFolder, AssetsFolderName, AudioFolderName));
			Directory.CreateDirectory(Path.Combine(projectFolder, RendersFolderName));

			var projectName = Path.GetFileName(projectFolder);
			var projectFilePath = Path.Combine(projectFolder, projectName + ".srproject");

			// Initialise blank project data
			_currentData = new ProjectData
			{
				ProjectName    = projectName,
				CreatedAt      = DateTime.UtcNow.ToString("o"),
				LastSavedAt    = DateTime.UtcNow.ToString("o"),
				FormatVersion  = ProjectData.CurrentFormatVersion,
			};

			// Write the project file
			WriteProjectFile(projectFilePath, _currentData);

			CurrentProjectFolder = projectFolder;
			CurrentProjectFile   = projectFilePath;
			CurrentProjectName   = projectName;
			ClearDirty();

		GD.Print($"ProjectManager: New project created at '{projectFolder}'");
		AddToRecentProjects(projectFilePath, projectName);
		ProjectOpened?.Invoke(projectFolder);
		return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.NewProject failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Opens an existing project from a .srproject file path.
	/// </summary>
	public static bool OpenProject(string projectFilePath)
	{
		if (!File.Exists(projectFilePath))
		{
			GD.PrintErr($"ProjectManager.OpenProject: file not found '{projectFilePath}'");
			return false;
		}

		try
		{
			var json = File.ReadAllText(projectFilePath);
			var data = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions);
			if (data == null)
			{
				GD.PrintErr("ProjectManager.OpenProject: failed to deserialise project file");
				return false;
			}

			_currentData         = data;
			CurrentProjectFile   = projectFilePath;
			CurrentProjectFolder = Path.GetDirectoryName(projectFilePath) ?? "";
			CurrentProjectName   = data.ProjectName;
			ClearDirty();

		GD.Print($"ProjectManager: Opened project '{CurrentProjectName}' from '{projectFilePath}'");
		AddToRecentProjects(projectFilePath, CurrentProjectName);
		ProjectOpened?.Invoke(CurrentProjectFolder);
		AssetsChanged?.Invoke();
		return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.OpenProject failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Saves the current project to its existing file.
	/// Returns false if no project is open.
	/// </summary>
	public static bool SaveProject()
	{
		if (string.IsNullOrEmpty(CurrentProjectFile))
		{
			GD.PrintErr("ProjectManager.SaveProject: no project is open");
			return false;
		}

		return SaveProjectAs(CurrentProjectFile);
	}

	/// <summary>
	/// Saves the current project to a specific file path.
	/// </summary>
	public static bool SaveProjectAs(string projectFilePath)
	{
		try
		{
			// Collect live scene state before writing
			CollectSceneState();

			_currentData.LastSavedAt = DateTime.UtcNow.ToString("o");
			WriteProjectFile(projectFilePath, _currentData);

			CurrentProjectFile = projectFilePath;
			ClearDirty();

			ProjectSaved?.Invoke();
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.SaveProjectAs failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Closes the current project and resets to a blank state.
	/// </summary>
	public static void CloseProject()
	{
		CurrentProjectFolder = "";
		CurrentProjectFile   = "";
		CurrentProjectName   = "Untitled";
		ClearDirty();
		_currentData         = new ProjectData();

		ProjectClosed?.Invoke();
	}

	/// <summary>Marks the project as having unsaved changes.</summary>
	public static void MarkDirty()
	{
		if (IsDirty) return; // already dirty – no need to fire again
		IsDirty = true;
		DirtyChanged?.Invoke(true);
	}

	/// <summary>Clears the dirty flag and fires <see cref="DirtyChanged"/>.</summary>
	private static void ClearDirty()
	{
		if (!IsDirty) return;
		IsDirty = false;
		DirtyChanged?.Invoke(false);
	}

	/// <summary>
	/// Fires the AssetsChanged event from outside the class.
	/// Use this when you mutate an AssetEntry directly (e.g. rename label).
	/// </summary>
	public static void NotifyAssetsChanged()
	{
		AssetsChanged?.Invoke();
	}

	// ── Asset management ──────────────────────────────────────────────────────

	/// <summary>
	/// Imports an external file into the project's assets folder.
	/// The file is copied into the appropriate sub-folder based on its extension.
	/// Returns the destination path, or empty string on failure.
	/// </summary>
	public static string ImportAsset(string sourcePath)
	{
		if (string.IsNullOrEmpty(CurrentProjectFolder))
		{
			GD.PrintErr("ProjectManager.ImportAsset: no project is open");
			return "";
		}

		if (!File.Exists(sourcePath))
		{
			GD.PrintErr($"ProjectManager.ImportAsset: source file not found '{sourcePath}'");
			return "";
		}

		try
		{
			var ext         = Path.GetExtension(sourcePath).ToLowerInvariant();
			var fileName    = Path.GetFileName(sourcePath);
			string destFolder;

			// .mimodel files get their own sub-folder so their internal assets
			// (textures, meshes, etc.) cannot collide with those of other .mimodel
			// packages that happen to contain files with the same names.
			if (ext == ".mimodel")
			{
				var baseName       = Path.GetFileNameWithoutExtension(fileName);
				var modelSubFolder = GetUniqueDirectoryPath(ModelsFolder, baseName);
				Directory.CreateDirectory(modelSubFolder);
				destFolder = modelSubFolder;
			}
			else
			{
				destFolder = GetAssetDestinationFolder(ext);
				Directory.CreateDirectory(destFolder);
			}

			var destPath  = GetUniqueFilePath(destFolder, fileName);

			File.Copy(sourcePath, destPath, overwrite: false);

			// For .mimodel files, also copy all referenced texture assets so the
			// model can be loaded from the project folder without needing the
			// original source directory.
			if (ext == ".mimodel")
			{
				CopyMiModelAssets(sourcePath, destFolder);
			}

			// Register in manifest
			var entry = new AssetEntry
			{
				FileName     = Path.GetFileName(destPath),
				RelativePath = GetRelativePath(CurrentProjectFolder, destPath),
				AssetType    = GetAssetType(ext),
				ImportedAt   = DateTime.UtcNow.ToString("o"),
			};

			_currentData.Assets.Add(entry);
			MarkDirty();
			AssetsChanged?.Invoke();

			GD.Print($"ProjectManager: Imported asset '{fileName}' → '{destPath}'");
			return destPath;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.ImportAsset failed: {ex.Message}");
			return "";
		}
	}

	/// <summary>
	/// Removes an asset from the project manifest (and optionally deletes the file).
	/// </summary>
	public static bool RemoveAsset(string relativePath, bool deleteFile = false)
	{
		var entry = _currentData.Assets.Find(a => a.RelativePath == relativePath);
		if (entry == null)
		{
			GD.PrintErr($"ProjectManager.RemoveAsset: asset not found '{relativePath}'");
			return false;
		}

		_currentData.Assets.Remove(entry);

		if (deleteFile)
		{
			var fullPath = Path.Combine(CurrentProjectFolder, relativePath);
			if (File.Exists(fullPath))
			{
				try { File.Delete(fullPath); }
				catch (Exception ex) { GD.PrintErr($"ProjectManager.RemoveAsset: delete failed: {ex.Message}"); }
			}
		}

		MarkDirty();
		AssetsChanged?.Invoke();
		return true;
	}

	// ── Audio track management ────────────────────────────────────────────────

	/// <summary>Returns all audio tracks in the current project.</summary>
	public static IReadOnlyList<AudioTrackData> GetAudioTracks() => _currentData.AudioTracks.AsReadOnly();

	/// <summary>
	/// Adds a new audio track to the project.
	/// <paramref name="relativePath"/> should be the asset's relative path (from <see cref="AssetEntry.RelativePath"/>).
	/// Returns the newly created <see cref="AudioTrackData"/>.
	/// </summary>
	public static AudioTrackData AddAudioTrack(string relativePath, string name = null, int startFrame = 0)
	{
		var track = new AudioTrackData
		{
			RelativePath = relativePath,
			Name         = name ?? System.IO.Path.GetFileNameWithoutExtension(relativePath),
			StartFrame   = startFrame,
		};
		_currentData.AudioTracks.Add(track);
		MarkDirty();
		AudioTracksChanged?.Invoke();
		return track;
	}

	/// <summary>Removes an audio track by its <see cref="AudioTrackData.Id"/>.</summary>
	public static bool RemoveAudioTrack(string trackId)
	{
		var track = _currentData.AudioTracks.Find(t => t.Id == trackId);
		if (track == null) return false;
		_currentData.AudioTracks.Remove(track);
		MarkDirty();
		AudioTracksChanged?.Invoke();
		return true;
	}

	/// <summary>Fired when the audio track list changes (add / remove / modify).</summary>
	public static event Action AudioTracksChanged;

	/// <summary>Fires <see cref="AudioTracksChanged"/> from outside the class (also marks dirty).</summary>
	public static void NotifyAudioTracksChanged()
	{
		MarkDirty();
		AudioTracksChanged?.Invoke();
	}

	/// <summary>
	/// Fires <see cref="AudioTracksChanged"/> without marking the project dirty.
	/// Used when derived/measured data (e.g. clip duration) is updated internally.
	/// </summary>
	internal static void NotifyAudioTracksChangedSilent()
	{
		AudioTracksChanged?.Invoke();
	}

	/// <summary>Returns all registered asset entries.</summary>
	public static IReadOnlyList<AssetEntry> GetAssets() => _currentData.Assets.AsReadOnly();

	/// <summary>Returns asset entries filtered by type.</summary>
	public static List<AssetEntry> GetAssetsByType(string assetType)
	{
		return _currentData.Assets.FindAll(a =>
			string.Equals(a.AssetType, assetType, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Returns the full absolute path for an asset entry.</summary>
	public static string GetAssetFullPath(AssetEntry entry)
	{
		if (string.IsNullOrEmpty(CurrentProjectFolder)) return "";
		return Path.Combine(CurrentProjectFolder, entry.RelativePath);
	}

	// ── Project settings accessors ────────────────────────────────────────────

	/// <summary>
	/// Returns the timeline frame number that was active when the project was last saved.
	/// Used by the restore system to seek the timeline to the correct position after loading.
	/// </summary>
	public static int GetSavedCurrentFrame() => _currentData.CurrentFrame;

	public static ProjectSettings GetSettings() => _currentData.Settings;

	public static void UpdateSettings(ProjectSettings settings)
	{
		_currentData.Settings = settings;
		MarkDirty();
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented          = true,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private static void WriteProjectFile(string path, ProjectData data)
	{
		var json = JsonSerializer.Serialize(data, JsonOptions);
		File.WriteAllText(path, json);
	}

	/// <summary>
	/// Walks the live scene and serialises all SceneObjects into _currentData.SceneObjects.
	/// Also saves the current timeline frame so it can be restored on load.
	/// </summary>
	private static void CollectSceneState()
	{
		_currentData.SceneObjects.Clear();

		// Save the current timeline frame number
		_currentData.CurrentFrame = TimelinePanel.Instance?.CurrentFrame ?? 0;

		var viewport = Main.Instance?.Viewport;
		if (viewport == null) return;

		foreach (var child in viewport.GetChildren())
		{
			if (child is SceneObject sceneObject)
				CollectSceneObjectRecursive(sceneObject, null);
		}
	}

	private static void CollectSceneObjectRecursive(SceneObject obj, string parentId)
	{
		// Capture the actual visual transform at the current frame (Position reflects keyframe
		// animation applied by the timeline, whereas LocalPosition is the rest-pose value).
		var actualPos   = obj.Position;
		var actualRot   = obj.Rotation;
		var actualScale = obj.Scale;

		var entry = new SceneObjectEntry
		{
			Id         = obj.ObjectId,
			ParentId   = parentId,
			Name       = obj.Name,
			ObjectType = obj.ObjectType,
			Visible    = obj.ObjectVisible,
			Position   = new float[] { obj.LocalPosition.X, obj.LocalPosition.Y, obj.LocalPosition.Z },
			Rotation   = new float[] { obj.LocalRotation.X, obj.LocalRotation.Y, obj.LocalRotation.Z },
			Scale      = new float[] { obj.LocalScale.X,    obj.LocalScale.Y,    obj.LocalScale.Z    },
			CurrentFramePosition = new float[] { actualPos.X,   actualPos.Y,   actualPos.Z   },
			CurrentFrameRotation = new float[] { actualRot.X,   actualRot.Y,   actualRot.Z   },
			CurrentFrameScale    = new float[] { actualScale.X, actualScale.Y, actualScale.Z },
		};

		// Store camera-specific properties
		if (obj is CameraSceneObject cameraObj)
		{
			entry.ExtraData["CameraFov"]  = cameraObj.Fov.ToString(System.Globalization.CultureInfo.InvariantCulture);
			entry.ExtraData["CameraNear"] = cameraObj.Near.ToString(System.Globalization.CultureInfo.InvariantCulture);
			entry.ExtraData["CameraFar"]  = cameraObj.Far.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		// Store spawn metadata so the object can be fully restored on load
		if (!string.IsNullOrEmpty(obj.SpawnCategory))
			entry.ExtraData["SpawnCategory"] = obj.SpawnCategory;

		if (!string.IsNullOrEmpty(obj.BlockVariant))
			entry.ExtraData["BlockVariant"] = obj.BlockVariant;

		if (!string.IsNullOrEmpty(obj.TextureType))
			entry.ExtraData["TextureType"] = obj.TextureType;

		// Store the source asset path so we can restore the model on load
		if (!string.IsNullOrEmpty(obj.SourceAssetPath))
		{
			// Store as a path relative to the project folder when possible
			if (!string.IsNullOrEmpty(CurrentProjectFolder))
			{
				try
				{
					var rel = GetRelativePath(CurrentProjectFolder, obj.SourceAssetPath);
					entry.ExtraData["AssetRelativePath"] = rel;
				}
				catch
				{
					entry.ExtraData["AssetRelativePath"] = obj.SourceAssetPath;
				}
			}
			else
			{
				entry.ExtraData["AssetRelativePath"] = obj.SourceAssetPath;
			}
		}

		// Serialise keyframes
		foreach (var kv in obj.Keyframes)
		{
			var frames = new List<KeyframeEntry>();
			foreach (var kf in kv.Value)
			{
				frames.Add(new KeyframeEntry
				{
					Frame              = kf.Frame,
					Value              = kf.Value?.ToString() ?? "",
					InterpolationType  = kf.InterpolationType,
				});
			}
			entry.Keyframes[kv.Key] = frames;
		}

		_currentData.SceneObjects.Add(entry);

		// Recurse into children
		foreach (var child in obj.GetChildrenObjects())
			CollectSceneObjectRecursive(child, obj.ObjectId);
	}

	private static readonly HashSet<string> PrimitiveTypes = new HashSet<string>(
		StringComparer.OrdinalIgnoreCase)
	{
		"Cube", "Sphere", "Cylinder", "Cone", "Torus", "Plane", "Capsule"
	};

	/// <summary>
	/// Restores the scene from the loaded project data asynchronously.
	/// Handles custom models (async load) and primitives (synchronous).
	/// Applies saved transforms and keyframes to each restored object.
	/// </summary>
	/// <param name="onProgress">
	/// Called before each object is loaded with (loadedCount, totalCount, objectName).
	/// </param>
	public static async System.Threading.Tasks.Task RestoreSceneStateAsync(
		Action<int, int, string> onProgress = null)
	{
		var viewport = Main.Instance?.Viewport;
		if (viewport == null)
		{
			GD.PrintErr("ProjectManager.RestoreSceneState: viewport not available");
			return;
		}

		// Clear existing user-placed SceneObjects from the viewport
		foreach (var child in viewport.GetChildren())
		{
			if (child is SceneObject)
				child.QueueFree();
		}

		// Wait one frame so all QueueFree'd nodes are actually removed before
		// we start spawning new objects.  Without this the scene tree still
		// contains the old nodes when the restore loop begins.
		await Main.Instance.ToSignal(
			Main.Instance.GetTree(),
			Godot.SceneTree.SignalName.ProcessFrame);

		// Collect top-level entries only (children are re-created by their parent's loader)
		var topLevel = new List<SceneObjectEntry>();
		foreach (var entry in _currentData.SceneObjects)
		{
			if (string.IsNullOrEmpty(entry.ParentId))
				topLevel.Add(entry);
		}

		int total = topLevel.Count;
		for (int i = 0; i < total; i++)
		{
			var entry = topLevel[i];
			onProgress?.Invoke(i, total, entry.Name);

			SceneObject restored = null;

			entry.ExtraData.TryGetValue("SpawnCategory", out var spawnCategory);
			entry.ExtraData.TryGetValue("BlockVariant",  out var blockVariant);
			entry.ExtraData.TryGetValue("TextureType",   out var textureType);

			// ── Custom model ──────────────────────────────────────────────────
			if (entry.ExtraData.TryGetValue("AssetRelativePath", out var relPath))
			{
				var absPath = Path.IsPathRooted(relPath)
					? relPath
					: Path.GetFullPath(Path.Combine(CurrentProjectFolder, relPath));

				if (!File.Exists(absPath))
				{
					GD.PrintErr($"ProjectManager.RestoreSceneState: asset not found '{absPath}'");
					continue;
				}

				// Count objects before spawn so we can find the new one afterwards
				int countBefore = CountSceneObjects(viewport);

				await Main.Instance.SpawnModelFromPathAsync(absPath);

				// Find the newly added SceneObject (last one added)
				restored = FindNewestSceneObject(viewport, countBefore);
			}
			// ── Light ─────────────────────────────────────────────────────────
			else if (entry.ObjectType == "Point Light" ||
			         string.Equals(spawnCategory, "Light", StringComparison.OrdinalIgnoreCase))
			{
				restored = Main.Instance?.SpawnLightObject(entry.Name);
			}
			// ── Minecraft block ───────────────────────────────────────────────
			else if (string.Equals(spawnCategory, "Blocks", StringComparison.OrdinalIgnoreCase))
			{
				restored = Main.Instance?.SpawnBlockObject(
					entry.ObjectType,
					blockVariant ?? "",
					entry.Name);
			}
			// ── Minecraft item / texture plane ────────────────────────────────
			else if (string.Equals(spawnCategory, "Items", StringComparison.OrdinalIgnoreCase))
			{
				restored = Main.Instance?.SpawnItemObject(
					entry.ObjectType,
					textureType ?? "item",
					entry.Name);
			}
			// ── Primitive ─────────────────────────────────────────────────────
			else if (PrimitiveTypes.Contains(entry.ObjectType) ||
			         string.Equals(spawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase))
			{
				restored = Main.Instance?.SpawnPrimitiveObject(entry.ObjectType, entry.Name);
			}
			// ── Camera ────────────────────────────────────────────────────────
			else if (entry.ObjectType == "Camera" ||
				        string.Equals(spawnCategory, "Camera", StringComparison.OrdinalIgnoreCase))
			{
				var cameraRestored = Main.Instance?.SpawnCameraObject(entry.Name);
				if (cameraRestored != null)
				{
					// Restore camera-specific properties
					if (entry.ExtraData.TryGetValue("CameraFov", out var fovStr) &&
					    float.TryParse(fovStr, System.Globalization.NumberStyles.Float,
					                  System.Globalization.CultureInfo.InvariantCulture, out var fov))
						cameraRestored.Fov = fov;

					if (entry.ExtraData.TryGetValue("CameraNear", out var nearStr) &&
					    float.TryParse(nearStr, System.Globalization.NumberStyles.Float,
					                  System.Globalization.CultureInfo.InvariantCulture, out var near))
						cameraRestored.Near = near;

					if (entry.ExtraData.TryGetValue("CameraFar", out var farStr) &&
					    float.TryParse(farStr, System.Globalization.NumberStyles.Float,
					                  System.Globalization.CultureInfo.InvariantCulture, out var far))
						cameraRestored.Far = far;
				}
				restored = cameraRestored;
			}
			else
			{
				GD.Print($"ProjectManager.RestoreSceneState: skipping '{entry.Name}' (type '{entry.ObjectType}' / category '{spawnCategory}' not supported)");
				continue;
			}

			// ── Apply saved state ─────────────────────────────────────────────
			if (restored != null)
			{
				// Rename to match saved name
				restored.Name = entry.Name;
	
				// Apply the actual transform that was captured at the saved frame.
				// For objects without keyframes this is their true visual position
				// (e.g. a camera placed at the work-camera position).
				// For objects with keyframes the timeline will re-apply the correct
				// value when SetCurrentFrame is called after all objects are loaded.
				if (entry.CurrentFramePosition is { Length: 3 })
					restored.SetLocalPosition(new Vector3(entry.CurrentFramePosition[0], entry.CurrentFramePosition[1], entry.CurrentFramePosition[2]));
				else if (entry.Position is { Length: 3 })
					restored.SetLocalPosition(new Vector3(entry.Position[0], entry.Position[1], entry.Position[2]));
	
				if (entry.CurrentFrameRotation is { Length: 3 })
					restored.SetLocalRotation(new Vector3(entry.CurrentFrameRotation[0], entry.CurrentFrameRotation[1], entry.CurrentFrameRotation[2]));
				else if (entry.Rotation is { Length: 3 })
					restored.SetLocalRotation(new Vector3(entry.Rotation[0], entry.Rotation[1], entry.Rotation[2]));
	
				if (entry.CurrentFrameScale is { Length: 3 })
					restored.SetLocalScale(new Vector3(entry.CurrentFrameScale[0], entry.CurrentFrameScale[1], entry.CurrentFrameScale[2]));
				else if (entry.Scale is { Length: 3 })
					restored.SetLocalScale(new Vector3(entry.Scale[0], entry.Scale[1], entry.Scale[2]));
	
				restored.SetObjectVisible(entry.Visible);
	
				// Restore keyframes for the top-level object
				ApplyKeyframesToObject(restored, entry);
	
				// Restore keyframes for all child SceneObjects (e.g. bones of a model).
				// When a model is spawned its child parts are re-created automatically,
				// so we match them by name against the saved child entries.
				RestoreChildKeyframes(restored, entry.Id);
			}
		}

		// Final progress update
		onProgress?.Invoke(total, total, "");
	}

	private static int CountSceneObjects(Node viewport)
	{
		int count = 0;
		foreach (var child in viewport.GetChildren())
			if (child is SceneObject) count++;
		return count;
	}

	/// <summary>
	/// Returns the SceneObject that was added to the viewport after <paramref name="countBefore"/>
	/// objects were already present.  Scans from the end of the children list.
	/// </summary>
	private static SceneObject FindNewestSceneObject(Node viewport, int countBefore)
	{
		var children = viewport.GetChildren();
		// Walk backwards to find the most recently added SceneObject
		for (int i = children.Count - 1; i >= 0; i--)
		{
			if (children[i] is SceneObject so)
				return so;
		}
		return null;
	}

	/// <summary>
	/// Copies the saved keyframes from <paramref name="entry"/> onto <paramref name="obj"/>.
	/// </summary>
	private static void ApplyKeyframesToObject(SceneObject obj, SceneObjectEntry entry)
	{
		foreach (var kv in entry.Keyframes)
		{
			var keyframeList = new List<ObjectKeyframe>();
			foreach (var kf in kv.Value)
			{
				keyframeList.Add(new ObjectKeyframe
				{
					Frame             = kf.Frame,
					Value             = kf.Value,
					InterpolationType = kf.InterpolationType,
				});
			}
			obj.Keyframes[kv.Key] = keyframeList;
		}
	}

	/// <summary>
	/// Walks all saved entries whose ParentId (directly or transitively) traces back to
	/// <paramref name="parentId"/> and applies their keyframes to the matching live
	/// SceneObject descendants of <paramref name="parent"/>, matched by name.
	/// This restores keyframes for model parts (e.g. bones) that are re-created
	/// automatically when a model is spawned.
	/// </summary>
	private static void RestoreChildKeyframes(SceneObject parent, string parentId)
	{
		// Find all saved entries that are direct children of parentId and recurse into each.
		foreach (var childEntry in _currentData.SceneObjects)
		{
			if (childEntry.ParentId != parentId)
				continue;

			RestoreChildKeyframesRecursive(parent, childEntry);
		}
	}

	/// <summary>
	/// Finds the live SceneObject descendant of <paramref name="root"/> whose name matches
	/// <paramref name="entry"/>.Name, applies keyframes, then recurses into its children.
	/// </summary>
	private static void RestoreChildKeyframesRecursive(SceneObject root, SceneObjectEntry entry)
	{
		// Search all descendants of root for a SceneObject with the matching name
		var match = FindDescendantByName(root, entry.Name);
		if (match != null && entry.Keyframes.Count > 0)
		{
			ApplyKeyframesToObject(match, entry);
		}

		// Recurse: find saved entries that are children of this entry
		foreach (var grandChildEntry in _currentData.SceneObjects)
		{
			if (grandChildEntry.ParentId != entry.Id)
				continue;

			RestoreChildKeyframesRecursive(root, grandChildEntry);
		}
	}

	/// <summary>
	/// Recursively searches the descendants of <paramref name="root"/> for a
	/// <see cref="SceneObject"/> whose <c>Name</c> equals <paramref name="name"/>.
	/// </summary>
	private static SceneObject FindDescendantByName(SceneObject root, string name)
	{
		foreach (var child in root.GetChildrenObjects())
		{
			if (child.Name == name)
				return child;

			var found = FindDescendantByName(child, name);
			if (found != null)
				return found;
		}
		return null;
	}

	/// <summary>
	/// Parses a .mimodel JSON file and copies every texture it references
	/// (top-level "texture" and per-part "texture" fields, recursively) from
	/// the model's source directory into <paramref name="destFolder"/>.
	/// Missing or already-copied textures are silently skipped.
	/// </summary>
	private static void CopyMiModelAssets(string mimodelSourcePath, string destFolder)
	{
		try
		{
			var sourceDir = Path.GetDirectoryName(mimodelSourcePath) ?? "";
			var jsonText  = File.ReadAllText(mimodelSourcePath);

			using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
			var root = doc.RootElement;

			// Collect all unique texture paths referenced in the model
			var texturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			CollectMiModelTexturePaths(root, texturePaths);

			foreach (var texRelPath in texturePaths)
			{
				if (string.IsNullOrWhiteSpace(texRelPath))
					continue;

				var texSourcePath = Path.Combine(sourceDir, texRelPath);
				if (!File.Exists(texSourcePath))
				{
					GD.PrintErr($"ProjectManager: mimodel texture not found, skipping: '{texSourcePath}'");
					continue;
				}

				// Preserve the relative sub-path so the model can still resolve it
				// (e.g. "textures/skin.png" → destFolder/textures/skin.png).
				var texDestPath = Path.Combine(destFolder, texRelPath);
				var texDestDir  = Path.GetDirectoryName(texDestPath) ?? destFolder;
				Directory.CreateDirectory(texDestDir);

				if (!File.Exists(texDestPath))
				{
					File.Copy(texSourcePath, texDestPath);
					GD.Print($"ProjectManager: Copied mimodel texture '{texRelPath}' → '{texDestPath}'");
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"ProjectManager.CopyMiModelAssets failed for '{mimodelSourcePath}': {ex.Message}");
		}
	}

	/// <summary>
	/// Recursively walks a JsonElement representing a mimodel (or part) and
	/// adds every "texture" string value it finds to <paramref name="paths"/>.
	/// </summary>
	private static void CollectMiModelTexturePaths(System.Text.Json.JsonElement element,
		HashSet<string> paths)
	{
		if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
		{
			foreach (var prop in element.EnumerateObject())
			{
				if (prop.Name.Equals("texture", StringComparison.OrdinalIgnoreCase)
				    && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
				{
					var val = prop.Value.GetString();
					if (!string.IsNullOrWhiteSpace(val))
						paths.Add(val);
				}
				else
				{
					CollectMiModelTexturePaths(prop.Value, paths);
				}
			}
		}
		else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
		{
			foreach (var item in element.EnumerateArray())
				CollectMiModelTexturePaths(item, paths);
		}
	}

	private static string GetAssetDestinationFolder(string ext)
	{
		return ext switch
		{
			".glb" or ".gltf" or ".mimodel" or ".blend" => ModelsFolder,
			".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" => ImagesFolder,
			".wav" or ".mp3" or ".ogg" => AudioFolder,
			_ => AssetsFolder,
		};
	}

	private static string GetAssetType(string ext)
	{
		return ext switch
		{
			".glb" or ".gltf" or ".mimodel" or ".blend" => "Model",
			".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" => "Image",
			".wav" or ".mp3" or ".ogg" => "Audio",
			_ => "Other",
		};
	}

	private static string GetUniqueFilePath(string folder, string fileName)
	{
		var dest = Path.Combine(folder, fileName);
		if (!File.Exists(dest)) return dest;

		var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
		var ext            = Path.GetExtension(fileName);
		var counter        = 1;

		while (File.Exists(dest))
		{
			dest = Path.Combine(folder, $"{nameWithoutExt}_{counter}{ext}");
			counter++;
		}

		return dest;
	}

	/// <summary>
	/// Returns a unique directory path inside <paramref name="parentFolder"/> using
	/// <paramref name="baseName"/> as the preferred name.  If a directory (or file)
	/// with that name already exists, a numeric suffix is appended until a free name
	/// is found.
	/// </summary>
	private static string GetUniqueDirectoryPath(string parentFolder, string baseName)
	{
		var dest    = Path.Combine(parentFolder, baseName);
		if (!Directory.Exists(dest) && !File.Exists(dest)) return dest;

		var counter = 1;
		while (Directory.Exists(dest) || File.Exists(dest))
		{
			dest = Path.Combine(parentFolder, $"{baseName}_{counter}");
			counter++;
		}

		return dest;
	}

	private static string GetRelativePath(string basePath, string fullPath)
	{
		var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
		var fullUri = new Uri(fullPath);
		return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString())
		           .Replace('/', Path.DirectorySeparatorChar);
	}
}

// ── Data models ───────────────────────────────────────────────────────────────

/// <summary>Root data object serialised to the .srproject file.</summary>
public class ProjectData
{
	public const int CurrentFormatVersion = 1;

	public int    FormatVersion  { get; set; } = CurrentFormatVersion;
	public string ProjectName    { get; set; } = "Untitled";
	public string CreatedAt      { get; set; } = "";
	public string LastSavedAt    { get; set; } = "";

	/// <summary>
	/// The timeline frame number that was active when the project was last saved.
	/// Used to restore the timeline position and re-apply keyframe values on load.
	/// </summary>
	public int CurrentFrame { get; set; } = 0;

	public ProjectSettings       Settings     { get; set; } = new ProjectSettings();
	public List<AssetEntry>      Assets       { get; set; } = new List<AssetEntry>();
	public List<SceneObjectEntry> SceneObjects { get; set; } = new List<SceneObjectEntry>();
	public List<AudioTrackData>  AudioTracks  { get; set; } = new List<AudioTrackData>();
}

/// <summary>Project-level settings (resolution, framerate, background, etc.).</summary>
public class ProjectSettings
{
	public int    RenderWidth    { get; set; } = 1920;
	public int    RenderHeight   { get; set; } = 1080;
	public float  Framerate      { get; set; } = 30f;
	public string BackgroundColor { get; set; } = "#939BFF";
	public string BackgroundImagePath { get; set; } = "";
	public bool   StretchBackground { get; set; } = true;
}

/// <summary>Manifest entry for a single imported asset file.</summary>
public class AssetEntry
{
	public string FileName     { get; set; } = "";
	/// <summary>Path relative to the project root folder.</summary>
	public string RelativePath { get; set; } = "";
	/// <summary>One of: Model, Image, Audio, Other.</summary>
	public string AssetType    { get; set; } = "Other";
	public string ImportedAt   { get; set; } = "";
	/// <summary>Optional user-supplied display label.</summary>
	public string Label        { get; set; } = "";
}

/// <summary>Serialised representation of a single SceneObject.</summary>
public class SceneObjectEntry
{
	public string   Id         { get; set; } = "";
	public string   ParentId   { get; set; } = "";
	public string   Name       { get; set; } = "";
	public string   ObjectType { get; set; } = "Object";
	public bool     Visible    { get; set; } = true;

	/// <summary>Rest-pose local position (before keyframe animation is applied).</summary>
	public float[]  Position   { get; set; } = new float[3];
	/// <summary>Rest-pose local rotation (before keyframe animation is applied).</summary>
	public float[]  Rotation   { get; set; } = new float[3];
	/// <summary>Rest-pose local scale (before keyframe animation is applied).</summary>
	public float[]  Scale      { get; set; } = new float[] { 1, 1, 1 };

	/// <summary>
	/// Actual position of the object at the frame when the project was saved.
	/// For objects without keyframes this captures transforms set via GlobalTransform
	/// (e.g. a camera spawned at the work-camera position) that are not reflected in
	/// the rest-pose <see cref="Position"/> field.
	/// </summary>
	public float[]  CurrentFramePosition { get; set; } = null;
	/// <summary>Actual rotation at the saved frame (radians).</summary>
	public float[]  CurrentFrameRotation { get; set; } = null;
	/// <summary>Actual scale at the saved frame.</summary>
	public float[]  CurrentFrameScale    { get; set; } = null;

	/// <summary>Key = property path, Value = list of keyframes.</summary>
	public Dictionary<string, List<KeyframeEntry>> Keyframes { get; set; } = new();

	/// <summary>Extra type-specific data (e.g. model path for a model object).</summary>
	public Dictionary<string, string> ExtraData { get; set; } = new();
}

/// <summary>A single keyframe value for a property.</summary>
public class KeyframeEntry
{
	public int    Frame             { get; set; }
	public string Value             { get; set; } = "";
	public string InterpolationType { get; set; } = "linear";
}

/// <summary>An entry in the recently-opened projects list.</summary>
public class RecentProjectEntry
{
	/// <summary>Absolute path to the .srproject file.</summary>
	public string FilePath    { get; set; } = "";
	/// <summary>Display name of the project.</summary>
	public string ProjectName { get; set; } = "";
	/// <summary>ISO-8601 UTC timestamp of when the project was last opened.</summary>
	public string LastOpened  { get; set; } = "";
}

/// <summary>Persistent user preferences stored in user data.</summary>
public class UserPrefs
{
	/// <summary>The folder path last used when creating a new project.</summary>
	public string LastNewProjectFolder { get; set; } = "";
}

/// <summary>
/// Represents a static audio track on the timeline.
/// The audio file starts playing at <see cref="StartFrame"/> and plays until it ends
/// (or the timeline loops).
/// </summary>
public class AudioTrackData
{
	/// <summary>Unique identifier for this track.</summary>
	public string Id { get; set; } = System.Guid.NewGuid().ToString();

	/// <summary>Display name shown in the timeline.</summary>
	public string Name { get; set; } = "Audio Track";

	/// <summary>
	/// Path relative to the project root folder (matches <see cref="AssetEntry.RelativePath"/>).
	/// </summary>
	public string RelativePath { get; set; } = "";

	/// <summary>The timeline frame at which playback of this clip begins.</summary>
	public int StartFrame { get; set; } = 0;

	/// <summary>
	/// Duration of the audio clip in frames (at the project frame rate).
	/// 0 means unknown / not yet measured.
	/// Populated by <see cref="simplyRemadeNuxi.core.AudioTrackManager"/> after the
	/// stream is loaded.
	/// </summary>
	public int DurationFrames { get; set; } = 0;

	/// <summary>The last frame occupied by this clip (StartFrame + DurationFrames - 1).</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public int EndFrame => DurationFrames > 0 ? StartFrame + DurationFrames - 1 : StartFrame;

	/// <summary>Playback volume in the range [0, 1].</summary>
	public float Volume { get; set; } = 1f;

	/// <summary>Whether this track is muted.</summary>
	public bool Muted { get; set; } = false;
}
