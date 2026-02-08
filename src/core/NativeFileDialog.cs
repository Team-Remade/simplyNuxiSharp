using Godot;
using System;

namespace simplyRemadeNuxi.core;

/// <summary>
/// A reusable wrapper for OS native file dialogs
/// </summary>
public static class NativeFileDialog
{
	/// <summary>
	/// Shows a native file dialog for opening a single file
	/// </summary>
	/// <param name="title">Dialog title</param>
	/// <param name="filters">File filters (e.g., new[] { "*.png", "*.jpg" })</param>
	/// <param name="callback">Callback when file is selected. Returns (success, filePath)</param>
	/// <param name="startDirectory">Starting directory (empty for default)</param>
	public static void ShowOpenFile(string title, string[] filters, Action<bool, string> callback, string startDirectory = "")
	{
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths[0]);
			}
			else
			{
				callback?.Invoke(false, "");
			}
		});
		
		DisplayServer.FileDialogShow(title, startDirectory, "", false, DisplayServer.FileDialogMode.OpenFile, filters, callable);
	}
	
	/// <summary>
	/// Shows a native file dialog for opening multiple files
	/// </summary>
	/// <param name="title">Dialog title</param>
	/// <param name="filters">File filters (e.g., new[] { "*.png", "*.jpg" })</param>
	/// <param name="callback">Callback when files are selected. Returns (success, filePaths)</param>
	/// <param name="startDirectory">Starting directory (empty for default)</param>
	public static void ShowOpenFiles(string title, string[] filters, Action<bool, string[]> callback, string startDirectory = "")
	{
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths);
			}
			else
			{
				callback?.Invoke(false, Array.Empty<string>());
			}
		});
		
		DisplayServer.FileDialogShow(title, startDirectory, "", false, DisplayServer.FileDialogMode.OpenFiles, filters, callable);
	}
	
	/// <summary>
	/// Shows a native file dialog for saving a file
	/// </summary>
	/// <param name="title">Dialog title</param>
	/// <param name="filters">File filters (e.g., new[] { "*.png", "*.jpg" })</param>
	/// <param name="callback">Callback when file path is selected. Returns (success, filePath)</param>
	/// <param name="startDirectory">Starting directory (empty for default)</param>
	/// <param name="defaultFileName">Default file name</param>
	public static void ShowSaveFile(string title, string[] filters, Action<bool, string> callback, string startDirectory = "", string defaultFileName = "")
	{
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths[0]);
			}
			else
			{
				callback?.Invoke(false, "");
			}
		});
		
		DisplayServer.FileDialogShow(title, startDirectory, defaultFileName, false, DisplayServer.FileDialogMode.SaveFile, filters, callable);
	}
	
	/// <summary>
	/// Shows a native file dialog for selecting a directory
	/// </summary>
	/// <param name="title">Dialog title</param>
	/// <param name="callback">Callback when directory is selected. Returns (success, directoryPath)</param>
	/// <param name="startDirectory">Starting directory (empty for default)</param>
	public static void ShowOpenDirectory(string title, Action<bool, string> callback, string startDirectory = "")
	{
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths[0]);
			}
			else
			{
				callback?.Invoke(false, "");
			}
		});
		
		DisplayServer.FileDialogShow(title, startDirectory, "", false, DisplayServer.FileDialogMode.OpenDir, Array.Empty<string>(), callable);
	}
	
	// Common filter presets for convenience
	public static class Filters
	{
		public static readonly string[] Images = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.svg" };
		public static readonly string[] ImagesCommon = new[] { "*.png", "*.jpg", "*.jpeg" };
		public static readonly string[] Audio = new[] { "*.wav", "*.mp3", "*.ogg" };
		public static readonly string[] Video = new[] { "*.mp4", "*.avi", "*.mov", "*.mkv" };
		public static readonly string[] Documents = new[] { "*.txt", "*.pdf", "*.doc", "*.docx" };
		public static readonly string[] Json = new[] { "*.json" };
		public static readonly string[] Xml = new[] { "*.xml" };
		public static readonly string[] CSharp = new[] { "*.cs" };
		public static readonly string[] GodotScene = new[] { "*.tscn", "*.scn" };
		public static readonly string[] GodotResource = new[] { "*.tres", "*.res" };
		public static readonly string[] All = new[] { "*" };
	}
}
