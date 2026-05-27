using Godot;
using System.Collections.Generic;

namespace PlayGround.Audio;

public partial class AudioManager : Node2D
{
	public const int DefaultMaxSimultaneousPerSound = 8;
	public const float DefaultSameSoundStartSpacingSeconds = 0.04f;

	[Export] public int max_active_players = 24;
	[Export] public float same_sound_start_spacing_seconds = DefaultSameSoundStartSpacingSeconds;

	private readonly List<AudioStreamPlayer2D> _players = new();
	private readonly Dictionary<string, AudioStreamPlayer2D> _playersByStream = new();
	private readonly Dictionary<string, double> _lastStartSecondsByStream = new();
	private long _accepted;
	private long _culledDuplicates;
	private long _culledCapacity;
	private long _rejected;

	public bool PlaySound(AudioStream? stream, Vector2 worldPosition, int priority = 0, int maxSimultaneous = DefaultMaxSimultaneousPerSound)
	{
		if (stream == null)
		{
			_rejected++;
			return false;
		}

		int allowedSimultaneous = Mathf.Max(maxSimultaneous, 0);
		string streamKey = StreamKey(stream);
		double nowSeconds = Time.GetTicksUsec() / 1000000.0;
		if (allowedSimultaneous == 0 || ShouldCullForSpacing(streamKey, nowSeconds))
		{
			_culledDuplicates++;
			return false;
		}

		AudioStreamPlayer2D? player = PlayerForStream(streamKey);
		if (player == null)
		{
			int cappedPlayerCount = Mathf.Max(max_active_players, 0);
			if (_playersByStream.Count >= cappedPlayerCount)
			{
				_culledCapacity++;
				return false;
			}

			player = UnassignedPlayer();
			if (player == null)
			{
				if (_players.Count >= cappedPlayerCount)
				{
					_culledCapacity++;
					return false;
				}

				player = CreatePlayer();
			}

			AssignPlayerToStream(player, streamKey);
		}

		player.Stream = stream;
		player.MaxPolyphony = allowedSimultaneous;
		player.GlobalPosition = worldPosition;
		player.SetMeta("priority", priority);
		player.Play();
		_lastStartSecondsByStream[streamKey] = nowSeconds;
		_accepted++;
		return true;
	}

	public bool play_sound(AudioStream? stream, Vector2 worldPosition, int priority = 0, int maxSimultaneous = DefaultMaxSimultaneousPerSound) =>
		PlaySound(stream, worldPosition, priority, maxSimultaneous);

	public int CurrentPlayingCount(AudioStream? stream)
	{
		if (stream == null)
		{
			return 0;
		}

		AudioStreamPlayer2D? player = PlayerForStream(StreamKey(stream));
		return player != null && player.Playing ? 1 : 0;
	}

	public int current_playing_count(AudioStream? stream) => CurrentPlayingCount(stream);

	public string StatsText()
	{
		return string.Format(
			System.Globalization.CultureInfo.InvariantCulture,
			"AudioManager: active_players:{0} pool:{1}/{2} streams:{3} accepted:{4} culled_spacing:{5} culled_capacity:{6} rejected:{7}",
			ActiveCount(),
			_players.Count,
			Mathf.Max(max_active_players, 0),
			_playersByStream.Count,
			_accepted,
			_culledDuplicates,
			_culledCapacity,
			_rejected)
			+ " polyphony:godot";
	}

	public string stats_text() => StatsText();

	public int ActiveCount()
	{
		int count = 0;
		foreach (AudioStreamPlayer2D player in _players)
		{
			if (player.Playing)
			{
				count++;
			}
		}
		return count;
	}

	public int active_count() => ActiveCount();

	public Variant GetRuntimeValue(StringName propertyName)
	{
		return propertyName.ToString() switch
		{
			"_accepted" => _accepted,
			"_culled_duplicates" => _culledDuplicates,
			"_culled_capacity" => _culledCapacity,
			"_rejected" => _rejected,
			"_active_count" => ActiveCount(),
			"_pool_count" => _players.Count,
			"_stream_count" => _playersByStream.Count,
			_ => default,
		};
	}

	public Variant get_runtime_value(StringName propertyName) => GetRuntimeValue(propertyName);

	public void Clear()
	{
		foreach (AudioStreamPlayer2D player in _players)
		{
			player.Stop();
			player.Stream = null;
			if (player.HasMeta("priority"))
			{
				player.RemoveMeta("priority");
			}
		}

		_playersByStream.Clear();
		_lastStartSecondsByStream.Clear();
		_accepted = 0;
		_culledDuplicates = 0;
		_culledCapacity = 0;
		_rejected = 0;
	}

	public void clear() => Clear();

	private AudioStreamPlayer2D? UnassignedPlayer()
	{
		foreach (AudioStreamPlayer2D player in _players)
		{
			if (!_playersByStream.ContainsValue(player))
			{
				return player;
			}
		}
		return null;
	}

	private bool ShouldCullForSpacing(string streamKey, double nowSeconds)
	{
		float spacingSeconds = Mathf.Max(same_sound_start_spacing_seconds, 0.0f);
		return spacingSeconds > 0.0f
			&& _lastStartSecondsByStream.TryGetValue(streamKey, out double lastStartSeconds)
			&& nowSeconds - lastStartSeconds < spacingSeconds;
	}

	private AudioStreamPlayer2D? PlayerForStream(string streamKey)
	{
		if (_playersByStream.TryGetValue(streamKey, out AudioStreamPlayer2D? player))
		{
			return player;
		}
		return null;
	}

	private AudioStreamPlayer2D CreatePlayer()
	{
		var player = new AudioStreamPlayer2D
		{
			Name = $"SoundPlayer{_players.Count + 1}",
		};
		AddChild(player);
		_players.Add(player);
		return player;
	}

	private void AssignPlayerToStream(AudioStreamPlayer2D player, string streamKey)
	{
		string? previousKey = null;
		foreach (KeyValuePair<string, AudioStreamPlayer2D> entry in _playersByStream)
		{
			if (entry.Value == player)
			{
				previousKey = entry.Key;
				break;
			}
		}

		if (previousKey != null)
		{
			_playersByStream.Remove(previousKey);
		}
		_playersByStream[streamKey] = player;
	}

	private static string StreamKey(AudioStream stream)
	{
		return !string.IsNullOrEmpty(stream.ResourcePath)
			? stream.ResourcePath
			: stream.GetInstanceId().ToString();
	}
}
