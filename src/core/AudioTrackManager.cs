using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace simplyRemadeNuxi.core;

/// <summary>
/// Singleton that manages <see cref="AudioStreamPlayer"/> nodes for every
/// <see cref="AudioTrackData"/> registered in the current project.
///
/// It listens to <see cref="ProjectManager.AudioTracksChanged"/> and
/// <see cref="ProjectManager.ProjectOpened"/> / <see cref="ProjectManager.ProjectClosed"/>
/// to keep its internal player pool in sync.
///
/// The <see cref="TimelinePanel"/> calls <see cref="SyncToFrame"/> every frame
/// during playback and scrubbing so that each audio clip starts / stops at the
/// correct timeline position.
/// </summary>
public partial class AudioTrackManager : Node
{
	// ── Singleton ─────────────────────────────────────────────────────────────

	private static AudioTrackManager _instance;
	public static AudioTrackManager Instance => _instance;

	// ── Internal state ────────────────────────────────────────────────────────

	/// <summary>Maps AudioTrackData.Id → the AudioStreamPlayer for that track.</summary>
	private readonly Dictionary<string, AudioStreamPlayer> _players = new();

	/// <summary>
	/// Tracks the last frame we synced to so we can detect direction changes
	/// and avoid restarting audio unnecessarily.
	/// </summary>
	private int _lastSyncedFrame = -1;

	/// <summary>Whether the timeline is currently playing (not paused / stopped).</summary>
	private bool _isPlaying = false;

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()
	{
		_instance = this;

		ProjectManager.ProjectOpened    += OnProjectChanged;
		ProjectManager.ProjectClosed    += OnProjectClosed;
		ProjectManager.AudioTracksChanged += OnAudioTracksChanged;

		RebuildPlayers();
	}

	public override void _ExitTree()
	{
		ProjectManager.ProjectOpened    -= OnProjectChanged;
		ProjectManager.ProjectClosed    -= OnProjectClosed;
		ProjectManager.AudioTracksChanged -= OnAudioTracksChanged;

		_instance = null;
	}

	// ── Event handlers ────────────────────────────────────────────────────────

	private void OnProjectChanged(string _) => RebuildPlayers();
	private void OnProjectClosed()          => RebuildPlayers();
	private void OnAudioTracksChanged()     => RebuildPlayers();

	// ── Public API ────────────────────────────────────────────────────────────

	/// <summary>
	/// Called by <see cref="TimelinePanel"/> when the frame changes or when
	/// playback state changes.  Starts / stops / seeks each audio player so
	/// it stays in sync with <paramref name="currentFrame"/> at
	/// <paramref name="frameRate"/> fps.
	/// </summary>
	/// <param name="forceSeek">
	/// When <c>true</c> (e.g. user scrubbed the playhead) the player is
	/// always seeked to the exact position even if it is already playing.
	/// When <c>false</c> (normal playback) the player is only seeked if the
	/// drift exceeds a generous tolerance so we never interrupt smooth playback.
	/// </param>
	public void SyncToFrame(int currentFrame, float frameRate, bool playing, bool forceSeek = false)
	{
		bool frameChanged = currentFrame != _lastSyncedFrame;
		bool stateChanged = playing != _isPlaying;

		// Nothing to do if nothing changed and we're not forcing a seek
		if (!frameChanged && !stateChanged && !forceSeek)
			return;

		_isPlaying = playing;
		_lastSyncedFrame = currentFrame;

		float currentTime = currentFrame / frameRate;

		foreach (var track in ProjectManager.GetAudioTracks())
		{
			if (!_players.TryGetValue(track.Id, out var player)) continue;
			if (player == null || player.Stream == null) continue;

			float trackStartTime = track.StartFrame / frameRate;
			float clipOffset     = currentTime - trackStartTime;
			float clipLength     = (float)player.Stream.GetLength();

			bool shouldBePlaying = playing
				&& !track.Muted
				&& clipOffset >= 0f
				&& (clipLength <= 0f || clipOffset < clipLength);

			if (shouldBePlaying)
			{
				if (!player.Playing)
				{
					// Start from the correct offset
					player.Play(clipOffset);
				}
				else if (forceSeek)
				{
					// User explicitly scrubbed — seek immediately
					player.Seek(clipOffset);
				}
				else
				{
					// During normal playback only seek if drift is large
					// (e.g. the timeline looped back to frame 0).
					// Use a generous tolerance (1 second) to avoid interrupting
					// smooth playback with micro-seeks.
					float drift = Mathf.Abs(player.GetPlaybackPosition() - clipOffset);
					if (drift > 1.0f)
					{
						player.Seek(clipOffset);
					}
				}
			}
			else
			{
				if (player.Playing)
					player.Stop();
			}
		}
	}

	/// <summary>Stops all audio players immediately.</summary>
	public void StopAll()
	{
		_isPlaying = false;
		foreach (var player in _players.Values)
			player?.Stop();
	}

	/// <summary>Pauses all currently-playing audio players.</summary>
	public void PauseAll()
	{
		_isPlaying = false;
		foreach (var player in _players.Values)
		{
			if (player != null && player.Playing)
				player.StreamPaused = true;
		}
	}

	/// <summary>Resumes all paused audio players.</summary>
	public void ResumeAll()
	{
		_isPlaying = true;
		foreach (var player in _players.Values)
		{
			if (player != null && player.StreamPaused)
				player.StreamPaused = false;
		}
	}

	// ── Player pool management ────────────────────────────────────────────────

	/// <summary>
	/// Destroys all existing <see cref="AudioStreamPlayer"/> children and
	/// re-creates one for every <see cref="AudioTrackData"/> in the project.
	/// </summary>
	private void RebuildPlayers()
	{
		// Stop and remove old players
		foreach (var player in _players.Values)
			player?.QueueFree();
		_players.Clear();

		if (string.IsNullOrEmpty(ProjectManager.CurrentProjectFolder))
			return;

		foreach (var track in ProjectManager.GetAudioTracks())
		{
			var player = CreatePlayerForTrack(track);
			if (player != null)
				_players[track.Id] = player;
		}
	}

	private AudioStreamPlayer CreatePlayerForTrack(AudioTrackData track)
	{
		if (string.IsNullOrEmpty(track.RelativePath)) return null;

		var fullPath = Path.Combine(ProjectManager.CurrentProjectFolder, track.RelativePath);
		if (!File.Exists(fullPath))
		{
			GD.PrintErr($"AudioTrackManager: audio file not found '{fullPath}'");
			return null;
		}

		AudioStream stream = LoadAudioStream(fullPath);
		if (stream == null)
		{
			GD.PrintErr($"AudioTrackManager: could not load audio stream '{fullPath}'");
			return null;
		}

		var player = new AudioStreamPlayer();
		player.Stream     = stream;
		player.VolumeDb   = Mathf.LinearToDb(Mathf.Clamp(track.Volume, 0f, 1f));
		player.Autoplay   = false;
		AddChild(player);

		// Measure the clip duration and store it in the track data so the
		// timeline can extend itself to accommodate the full clip length.
		double clipLengthSec = stream.GetLength();
		if (clipLengthSec > 0.0)
		{
			// We need the project frame rate; fall back to 30 if not available.
			float frameRate = ProjectManager.GetSettings()?.Framerate ?? 30f;
			if (frameRate <= 0f) frameRate = 30f;
			int durationFrames = Mathf.CeilToInt((float)(clipLengthSec * frameRate));
			if (track.DurationFrames != durationFrames)
			{
				track.DurationFrames = durationFrames;
				// Notify so the timeline can extend itself without marking dirty.
				ProjectManager.NotifyAudioTracksChangedSilent();
			}
		}

		GD.Print($"AudioTrackManager: created player for '{track.Name}' ({Path.GetFileName(fullPath)}) duration={track.DurationFrames} frames");
		return player;
	}

	/// <summary>
	/// Loads an <see cref="AudioStream"/> from an absolute file-system path.
	/// Supports .wav, .mp3, and .ogg.
	/// </summary>
	private static AudioStream LoadAudioStream(string absolutePath)
	{
		var ext = Path.GetExtension(absolutePath).ToLowerInvariant();

		try
		{
			var bytes = File.ReadAllBytes(absolutePath);

			switch (ext)
			{
				case ".wav":
				{
					var stream = new AudioStreamWav();
					// AudioStreamWav.LoadFromBuffer is not available in Godot 4 C# directly;
					// use the resource loader via a temporary res:// path trick or load raw.
					// Fallback: use ResourceLoader if the file is inside res://, otherwise
					// we must use the GDScript-side loader.  For user files we use the
					// AudioStreamWav data property approach.
					// Godot 4.x exposes AudioStreamWav.Data (PackedByteArray).
					stream.Data   = bytes;
					stream.Format = AudioStreamWav.FormatEnum.Format16Bits;
					return stream;
				}

				case ".mp3":
				{
					var stream = new AudioStreamMP3();
					stream.Data = bytes;
					return stream;
				}

				case ".ogg":
				{
					var stream = AudioStreamOggVorbis.LoadFromBuffer(bytes);
					return stream;
				}

				default:
					GD.PrintErr($"AudioTrackManager: unsupported audio format '{ext}'");
					return null;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"AudioTrackManager.LoadAudioStream failed for '{absolutePath}': {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Updates the volume of the player for the given track without rebuilding.
	/// </summary>
	public void UpdateTrackVolume(string trackId, float volume)
	{
		if (_players.TryGetValue(trackId, out var player) && player != null)
			player.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(volume, 0f, 1f));
	}

	/// <summary>
	/// Mutes or un-mutes the player for the given track without rebuilding.
	/// </summary>
	public void UpdateTrackMute(string trackId, bool muted)
	{
		if (_players.TryGetValue(trackId, out var player) && player != null)
		{
			if (muted && player.Playing)
				player.Stop();
		}
	}
}
