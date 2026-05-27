using Godot;
using System;
using PlayGround.Spawn;

public partial class Debug : CanvasLayer
{
	private static readonly NodePath DefaultMobSpawnerRootPath = new("../MobSpawnner");
	private static readonly NodePath LegacyWallMobSpawnerRootPath = new("../PlayAreaWall/MobSpawnner");

	[Export] public NodePath debug_label_path = new("DebugLabel");
	[Export] public NodePath player_path = new("../Player");
	[Export] public NodePath mob_spawner_root_path = DefaultMobSpawnerRootPath;
	[Export] public NodePath player_projectile_root_path = new("../ProjectileManager");
	[Export] public NodePath mob_projectile_root_path = new("../MobProjectileManager");
	[Export] public NodePath player_aoe_root_path = new("../AoeManager");
	[Export] public NodePath mob_aoe_root_path = new("../MobAoeManager");
	[Export] public bool enable_tracking_timing;

	private Label _debugLabel = null!;
	private Node2D _player = null!;
	private MobSpawnerRoot _mobSpawnerRoot = null!;
	private Root _playerProjectileRoot = null!;
	private Root _mobProjectileRoot = null!;
	private AoeRoot _playerAoeRoot = null!;
	private AoeRoot _mobAoeRoot = null!;

	public override void _Ready()
	{
		_debugLabel = GetNodeOrNull<Label>(debug_label_path);
		if (_debugLabel == null)
		{
			throw new InvalidOperationException($"Debug layer requires a Label at '{debug_label_path}'.");
		}

		_player = GetNodeOrNull<Node2D>(player_path);
		if (_player == null)
		{
			throw new InvalidOperationException($"Debug layer requires a player Node2D at '{player_path}'.");
		}

		MobSpawnerRoot? mobSpawnerRoot = ResolveMobSpawnerRoot();
		if (mobSpawnerRoot == null)
		{
			throw new InvalidOperationException($"Debug layer requires a MobSpawnerRoot at '{mob_spawner_root_path}'.");
		}
		_mobSpawnerRoot = mobSpawnerRoot;

		_playerProjectileRoot = GetNodeOrNull<Root>(player_projectile_root_path);
		if (_playerProjectileRoot == null)
		{
			throw new InvalidOperationException($"Debug layer requires a player projectile root at '{player_projectile_root_path}'.");
		}

		_mobProjectileRoot = GetNodeOrNull<Root>(mob_projectile_root_path);
		if (_mobProjectileRoot == null)
		{
			throw new InvalidOperationException($"Debug layer requires a mob projectile root at '{mob_projectile_root_path}'.");
		}

		_playerAoeRoot = GetNodeOrNull<AoeRoot>(player_aoe_root_path);
		if (_playerAoeRoot == null)
		{
			throw new InvalidOperationException($"Debug layer requires a player AOE root at '{player_aoe_root_path}'.");
		}

		_mobAoeRoot = GetNodeOrNull<AoeRoot>(mob_aoe_root_path);
		if (_mobAoeRoot == null)
		{
			throw new InvalidOperationException($"Debug layer requires a mob AOE root at '{mob_aoe_root_path}'.");
		}
	}

	public override void _Process(double delta)
	{
		_debugLabel.Text = DebugText();
	}

	private string DebugText()
	{
		Vector2 playerPosition = _player.GlobalPosition;
		double fps = Engine.GetFramesPerSecond();

		return string.Join("\n", new[]
		{
			$"FPS: {fps:0}",
			$"Player: ({playerPosition.X:0.0}, {playerPosition.Y:0.0})",
			$"Mobs: {_mobSpawnerRoot.ActiveMobCount()}",
			$"Projectiles: {_playerProjectileRoot.ActiveCount()}",
			$"Mob Projectiles: {_mobProjectileRoot.ActiveCount()}",
			$"AOEs: {_playerAoeRoot.ActiveCount()}",
			$"Mob AOEs: {_mobAoeRoot.ActiveCount()}",
		});
	}

	private MobSpawnerRoot? ResolveMobSpawnerRoot()
	{
		MobSpawnerRoot? configuredRoot = GetNodeOrNull<MobSpawnerRoot>(mob_spawner_root_path);
		if (configuredRoot != null)
		{
			return configuredRoot;
		}

		if (mob_spawner_root_path == DefaultMobSpawnerRootPath)
		{
			return GetNodeOrNull<MobSpawnerRoot>(LegacyWallMobSpawnerRootPath);
		}

		return null;
	}

	private static string FormatTrackingTotal(long totalMicroseconds)
	{
		return totalMicroseconds >= 1000
			? $"{(totalMicroseconds / 1000.0):0.00}ms"
			: $"{totalMicroseconds}us";
	}
}
