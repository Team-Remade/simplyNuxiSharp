using Godot;
using System;
using System.Collections.Generic;

namespace simplyRemadeNuxi.core;

/// <summary>
/// A reusable wrapper for OS native file dialogs
/// </summary>
public static class NativeFileDialog
{
	// Track active dialog callbacks so we can clean them up on app close
	private static readonly List<Action> _activeDialogCleanups = new List<Action>();
	private static int _activeDialogCount = 0;
	
	/// <summary>
	/// Closes all active file dialogs. Should be called when the application is closing.
	/// </summary>
	public static void CloseAllDialogs()
	{
		// Clear the callback list - the native dialogs will be closed by the OS
		// when the application window closes
		_activeDialogCleanups.Clear();
		_activeDialogCount = 0;
		
		// Re-enable the main window
		var window = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0);
		if (window != 0)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, 0);
		}
	}
	
	private static void OnDialogOpened()
	{
		_activeDialogCount++;
		// Disable the main window to prevent interaction
		var window = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0);
		if (window != 0)
		{
			// Set the window to be non-interactive by setting it to exclusive mode
			DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true, 0);
		}
	}
	
	private static void OnDialogClosed()
	{
		_activeDialogCount--;
		if (_activeDialogCount <= 0)
		{
			_activeDialogCount = 0;
			// Re-enable the main window
			var window = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0);
			if (window != 0)
			{
				DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, false, 0);
			}
		}
	}
	
	/// <summary>
	/// Shows a native file dialog for opening a single file
	/// </summary>
	/// <param name="title">Dialog title</param>
	/// <param name="filters">File filters (e.g., new[] { "*.png", "*.jpg" })</param>
	/// <param name="callback">Callback when file is selected. Returns (success, filePath)</param>
	/// <param name="startDirectory">Starting directory (empty for default)</param>
	public static void ShowOpenFile(string title, string[] filters, Action<bool, string> callback, string startDirectory = "")
	{
		Action cleanup = null;
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			// Remove cleanup action when dialog completes
			if (cleanup != null)
			{
				_activeDialogCleanups.Remove(cleanup);
			}
			
			OnDialogClosed();
			
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths[0]);
			}
			else
			{
				callback?.Invoke(false, "");
			}
		});
		
		// Track this dialog for cleanup
		cleanup = () => { /* Dialog cleanup placeholder */ };
		_activeDialogCleanups.Add(cleanup);
		
		OnDialogOpened();
		
		// Show as modal (true parameter) to block input to parent window
		DisplayServer.FileDialogShow(title, startDirectory, "", true, DisplayServer.FileDialogMode.OpenFile, filters, callable);
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
		Action cleanup = null;
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			// Remove cleanup action when dialog completes
			if (cleanup != null)
			{
				_activeDialogCleanups.Remove(cleanup);
			}
			
			OnDialogClosed();
			
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths);
			}
			else
			{
				callback?.Invoke(false, Array.Empty<string>());
			}
		});
		
		// Track this dialog for cleanup
		cleanup = () => { /* Dialog cleanup placeholder */ };
		_activeDialogCleanups.Add(cleanup);
		
		OnDialogOpened();
		
		// Show as modal (true parameter) to block input to parent window
		DisplayServer.FileDialogShow(title, startDirectory, "", true, DisplayServer.FileDialogMode.OpenFiles, filters, callable);
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
		Action cleanup = null;
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			// Remove cleanup action when dialog completes
			if (cleanup != null)
			{
				_activeDialogCleanups.Remove(cleanup);
			}
			
			OnDialogClosed();
			
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths[0]);
			}
			else
			{
				callback?.Invoke(false, "");
			}
		});
		
		// Track this dialog for cleanup
		cleanup = () => { /* Dialog cleanup placeholder */ };
		_activeDialogCleanups.Add(cleanup);
		
		OnDialogOpened();
		
		// Show as modal (true parameter) to block input to parent window
		DisplayServer.FileDialogShow(title, startDirectory, defaultFileName, true, DisplayServer.FileDialogMode.SaveFile, filters, callable);
	}
	
	/// <summary>
	/// Shows a native file dialog for selecting a directory
	/// </summary>
	/// <param name="title">Dialog title</param>
	/// <param name="callback">Callback when directory is selected. Returns (success, directoryPath)</param>
	/// <param name="startDirectory">Starting directory (empty for default)</param>
	public static void ShowOpenDirectory(string title, Action<bool, string> callback, string startDirectory = "")
	{
		Action cleanup = null;
		var callable = Callable.From<bool, string[], int>((status, paths, filterIndex) =>
		{
			// Remove cleanup action when dialog completes
			if (cleanup != null)
			{
				_activeDialogCleanups.Remove(cleanup);
			}
			
			OnDialogClosed();
			
			if (status && paths != null && paths.Length > 0)
			{
				callback?.Invoke(true, paths[0]);
			}
			else
			{
				callback?.Invoke(false, "");
			}
		});
		
		// Track this dialog for cleanup
		cleanup = () => { /* Dialog cleanup placeholder */ };
		_activeDialogCleanups.Add(cleanup);
		
		OnDialogOpened();
		
		// Show as modal (true parameter) to block input to parent window
		DisplayServer.FileDialogShow(title, startDirectory, "", true, DisplayServer.FileDialogMode.OpenDir, Array.Empty<string>(), callable);
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
		public static readonly string[] Glb = new[] { "*.glb", "*.gltf" };
		public static readonly string[] All = new[] { "*" };
	}
}
