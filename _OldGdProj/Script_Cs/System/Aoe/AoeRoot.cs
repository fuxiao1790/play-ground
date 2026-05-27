using Godot;
using System;

/// <summary>
/// Godot adapter root for one scoped AOE runtime and target group.
/// </summary>
public partial class AoeRoot : Node2D
{
	[Export] public StringName target_group = null!;
	[Export] public int maximumAoeListSize = 100000;
	[Export] public int maximumTargetListSize = 100;

	private readonly AoeWorld _world = new();
	private readonly EntityRegistry _registry = new();
	private AoeRenderer _renderer = null!;
	private AoeTypeRegistry _typeRegistry = null!;
	private AoeTargetSync _targetSync = null!;
	private AoeSpawnAdapter _spawnAdapter = null!;
	private bool _bound;

	public override void _Ready()
	{
		BindOrThrow();
		RequireConfiguredGroup(target_group, nameof(target_group));
		ApplyWorldRuntimeSettings();
		_world.Clear();
		_targetSync.Clear();
		_spawnAdapter.Clear();
		_renderer.Reset();
	}

	public override void _Process(double delta)
	{
		_spawnAdapter.ReplayHits();
		_renderer.EndFrame();
	}

	public override void _PhysicsProcess(double delta)
	{
		ApplyWorldRuntimeSettings();
		_targetSync.Sync(GetTree(), target_group);
		_world.Step(delta);
		_spawnAdapter.DrainEvents();
	}

	public int RegisterAoeType(PackedScene effectScene, NodePath collisionShapePath)
	{
		BindOrThrow();
		return _typeRegistry.RegisterAoeType(effectScene, collisionShapePath, maximumAoeListSize);
	}

	public void SpawnAoe(in AoeSpawnRequest request)
	{
		BindOrThrow();
		_spawnAdapter.SpawnAoe(in request);
	}

	public void SpawnAoe(
		Node source,
		int aoeTypeId,
		Vector2 position,
		int targetMask,
		float lifetimeSeconds,
		int damage)
	{
		SpawnAoe(
			new AoeSpawnRequest(
				source,
				aoeTypeId,
				position,
				targetMask,
				DamageSnapshot.Single(Mathf.Max(1, damage)),
				lifetimeSeconds));
	}

	public void SpawnTickingAoe(
		Node source,
		int aoeTypeId,
		Vector2 position,
		int targetMask,
		float lifetimeSeconds,
		float tickIntervalSeconds,
		int damage)
	{
		SpawnAoe(
			new AoeSpawnRequest(
				source,
				aoeTypeId,
				position,
				targetMask,
				DamageSnapshot.Single(Mathf.Max(1, damage)),
				lifetimeSeconds,
				tickIntervalSeconds));
	}

	public void SpawnAoeWithListener(
		Node source,
		int aoeTypeId,
		Vector2 position,
		int targetMask,
		float lifetimeSeconds,
		int damage,
		Node hitListener)
	{
		SpawnAoe(
			new AoeSpawnRequest(
				source,
				aoeTypeId,
				position,
				targetMask,
				DamageSnapshot.Single(Mathf.Max(1, damage)),
				lifetimeSeconds,
				hitListener: RequireAoeListener(hitListener)));
	}

	public void SpawnTickingAoeWithListener(
		Node source,
		int aoeTypeId,
		Vector2 position,
		int targetMask,
		float lifetimeSeconds,
		float tickIntervalSeconds,
		int damage,
		Node hitListener)
	{
		SpawnAoe(
			new AoeSpawnRequest(
				source,
				aoeTypeId,
				position,
				targetMask,
				DamageSnapshot.Single(Mathf.Max(1, damage)),
				lifetimeSeconds,
				tickIntervalSeconds,
				RequireAoeListener(hitListener)));
	}

	public int ActiveCount()
	{
		return _world.ActiveCount;
	}

	public void Clear()
	{
		BindOrThrow();
		_targetSync.Clear();
		_spawnAdapter.Clear();
		_world.Clear();
		_renderer.Reset();
	}

	private void ApplyWorldRuntimeSettings()
	{
		_world.MaximumAoeCount = maximumAoeListSize;
		_world.MaximumTargetCount = maximumTargetListSize;
	}

	private void BindOrThrow()
	{
		if (_bound)
		{
			return;
		}

		_renderer = GetNodeOrNull<AoeRenderer>("MultiMeshInstance2D");
		if (_renderer == null)
		{
			_renderer = new AoeRenderer
			{
				Name = "MultiMeshInstance2D"
			};
			AddChild(_renderer);
		}

		_typeRegistry = new AoeTypeRegistry(_world, _renderer);
		_targetSync = new AoeTargetSync(_world, _registry, _typeRegistry);
		_spawnAdapter = new AoeSpawnAdapter(_world, _registry, _typeRegistry, _renderer);
		_bound = true;
	}

	private static Aoe RequireAoeListener(Node hitListener)
	{
		if (hitListener is Aoe aoe)
		{
			return aoe;
		}

		throw new InvalidOperationException(
			$"AOE hit listener node '{hitListener?.Name}' must implement {nameof(Aoe)}.");
	}

	private static void RequireConfiguredGroup(StringName groupName, string propertyName)
	{
		if (groupName.ToString() == string.Empty)
		{
			throw new InvalidOperationException(
				$"AOE root requires '{propertyName}' to be configured in the scene.");
		}
	}
}
