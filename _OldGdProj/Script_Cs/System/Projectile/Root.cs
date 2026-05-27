using Godot;
using System;

/// <summary>
/// Godot adapter root for one scoped projectile runtime and target group.
/// </summary>
public partial class Root : Node2D
{
	public const int PlayerBodyLayer = 1 << 0;
	public const int PlayerHitboxLayer = 1 << 1;
	public const int PlayerProjectileLayer = 1 << 2;
	public const int PlayerAoeLayer = 1 << 3;

	public const int MobBodyLayer = 1 << 10;
	public const int MobHitboxLayer = 1 << 11;
	public const int MobProjectileLayer = 1 << 12;
	public const int MobAoeLayer = 1 << 13;

	public const int PlayerLayer = PlayerHitboxLayer;
	public const int MobLayer = MobHitboxLayer;

	[Export] public StringName target_group = null!;
	[Export] public int maximumProjectileListSize = 100000;
	[Export] public int initialProjectileListSize = 1000;
	[Export] public int initialTargetListSize = 20;
	[Export] public int maximumTargetListSize = 100;
	[Export] public float tracking_query_interval_seconds = 0.0f;
	[Export] public float tracking_query_interval_spawn_jitter_seconds = 0.05f;
	[Export] public bool projectile_threading_enabled = false;
	[Export] public int minimum_parallel_projectile_count = 2048;
	[Export] public int projectile_parallel_chunk_size = 512;
	[Export] public bool projectile_render_threading_enabled = false;
	[Export] public int minimum_parallel_render_count = 2048;
	[Export] public int projectile_render_parallel_chunk_size = 512;

	private Renderer _renderer = null!;
	private readonly ProjectileWorld _world = new(new Collision.SpatialHashFilter());
	private readonly EntityRegistry _registry = new();
	private ProjectileTypeRegistry _typeRegistry = null!;
	private ProjectileTargetSync _targetSync = null!;
	private ProjectileEventReplayer _eventReplayer = null!;
	private ProjectileSpawnAdapter _spawnAdapter = null!;
	private bool _childrenBound;

	public override void _Ready()
	{
		BindChildrenOrThrow();
		RequireConfiguredGroup(target_group, nameof(target_group));

		ApplyWorldRuntimeSettings();
		_world.Clear();
		_eventReplayer.ClearEventsAndListeners();
		_targetSync.Clear();
		_spawnAdapter.Randomize();
		_renderer.Reset();
	}

	public override void _Process(double delta)
	{
		_eventReplayer.ReplayHits();
		_renderer.Apply(
			_world.ActiveProjectiles,
			0,
			_world.ActiveProjectiles.Count,
			projectile_render_threading_enabled,
			Math.Max(1, minimum_parallel_render_count),
			Math.Max(1, projectile_render_parallel_chunk_size));
	}

	public override void _PhysicsProcess(double delta)
	{
		ApplyWorldRuntimeSettings();
		_targetSync.Sync(GetTree(), target_group);
		_world.Step(delta);
		_eventReplayer.Drain(
			_spawnAdapter.SpawnChildrenFromRequest,
			_spawnAdapter.ReleaseChildSpawnDefinition);
	}

	public int RegisterProjectileType(PackedScene template, NodePath collisionShapePath)
	{
		BindChildrenOrThrow();
		return _typeRegistry.RegisterProjectileType(
			template,
			collisionShapePath,
			maximumProjectileListSize);
	}

	public void SpawnProjectile(in ProjectileSpawnRequest request)
	{
		BindChildrenOrThrow();
		_spawnAdapter.SpawnProjectile(
			request,
			tracking_query_interval_seconds,
			tracking_query_interval_spawn_jitter_seconds);
	}

	public void SpawnProjectile(
		Node source,
		int projectileTypeId,
		Vector2 position,
		Vector2 velocity,
		float lifetime,
		int targetMask)
	{
		SpawnProjectile(
			new ProjectileSpawnRequest(
				source,
				projectileTypeId,
				position,
				velocity,
				lifetime,
				targetMask));
	}

	public void SpawnProjectileWithListener(
		Node source,
		int projectileTypeId,
		Vector2 position,
		Vector2 velocity,
		float lifetime,
		int targetMask,
		Node hitListener)
	{
		SpawnProjectile(
			new ProjectileSpawnRequest(
				source,
				projectileTypeId,
				position,
				velocity,
				lifetime,
				targetMask,
				RequireProjectileListener(hitListener)));
	}

	public int RegisterChildSpawnDefinition(
		Node source,
		int projectileTypeId,
		int childCount,
		float speed,
		float lifetime,
		int targetMask,
		Projectile? hitListener,
		in ProjectileTrackingConfig trackingConfig,
		DamageSnapshot? damage,
		ProjectileChildSpawnPattern pattern,
		int parentProjectileCount)
	{
		return RegisterChildSpawnDefinition(
			source,
			projectileTypeId,
			childCount,
			speed,
			lifetime,
			targetMask,
			hitListener,
			trackingConfig,
			damage,
			pattern,
			parentProjectileCount,
			0);
	}

	public int RegisterChildSpawnDefinition(
		Node source,
		int projectileTypeId,
		int childCount,
		float speed,
		float lifetime,
		int targetMask,
		Projectile? hitListener,
		in ProjectileTrackingConfig trackingConfig,
		DamageSnapshot? damage,
		ProjectileChildSpawnPattern pattern,
		int parentProjectileCount,
		int projectilePierceCount)
	{
		BindChildrenOrThrow();
		return _spawnAdapter.RegisterChildSpawnDefinition(
			source,
			projectileTypeId,
			childCount,
			speed,
			lifetime,
			targetMask,
			hitListener,
			trackingConfig,
			damage,
			pattern,
			parentProjectileCount,
			tracking_query_interval_seconds,
			tracking_query_interval_spawn_jitter_seconds,
			projectilePierceCount);
	}

	public void SpawnTrackingProjectile(
		Node source,
		int projectileTypeId,
		Vector2 position,
		Vector2 velocity,
		float lifetime,
		int targetMask,
		bool trackingEnabled,
		float trackingRange,
		float trackingTurnSpeedDegrees)
	{
		SpawnProjectile(
			new ProjectileSpawnRequest(
				source,
				projectileTypeId,
				position,
				velocity,
				lifetime,
				targetMask,
				trackingConfig: new ProjectileTrackingConfig(
					trackingEnabled,
					trackingRange,
					trackingTurnSpeedDegrees)));
	}

	public void SpawnTrackingProjectileWithListener(
		Node source,
		int projectileTypeId,
		Vector2 position,
		Vector2 velocity,
		float lifetime,
		int targetMask,
		Node hitListener,
		bool trackingEnabled,
		float trackingRange,
		float trackingTurnSpeedDegrees)
	{
		SpawnProjectile(
			new ProjectileSpawnRequest(
				source,
				projectileTypeId,
				position,
				velocity,
				lifetime,
				targetMask,
				RequireProjectileListener(hitListener),
				new ProjectileTrackingConfig(
					trackingEnabled,
					trackingRange,
					trackingTurnSpeedDegrees)));
	}

	public void SpawnTrackingProjectile(
		Node source,
		int projectileTypeId,
		Vector2 position,
		Vector2 velocity,
		float lifetime,
		int targetMask,
		Projectile? hitListener,
		bool trackingEnabled,
		float trackingRange,
		float trackingTurnSpeedDegrees)
	{
		SpawnProjectile(
			new ProjectileSpawnRequest(
				source,
				projectileTypeId,
				position,
				velocity,
				lifetime,
				targetMask,
				hitListener,
				new ProjectileTrackingConfig(
					trackingEnabled,
					trackingRange,
					trackingTurnSpeedDegrees)));
	}

	private static Projectile RequireProjectileListener(Node hitListener)
	{
		if (hitListener is Projectile projectile)
		{
			return projectile;
		}

		throw new InvalidOperationException(
			$"Hit listener node '{hitListener?.Name}' must implement {nameof(Projectile)}.");
	}

	public int ActiveCount()
	{
		return _world.ActiveCount;
	}

	public void SetProjectileThreadingEnabled(bool enabled)
	{
		projectile_threading_enabled = enabled;
		_world.ParallelSimulationEnabled = enabled;
	}

	public void SetProjectileRenderThreadingEnabled(bool enabled)
	{
		projectile_render_threading_enabled = enabled;
	}

	public bool WasLastProjectileStepThreaded()
	{
		return _world.LastStepUsedThreading;
	}

	public long GetLastProjectileStepMicroseconds()
	{
		return _world.LastStepMicroseconds;
	}

	public long GetLastThreadedProjectileStepMicroseconds()
	{
		return _world.LastThreadedStepMicroseconds;
	}

	public int GetLastThreadedProjectileWorkerCount()
	{
		return _world.LastThreadedWorkerCount;
	}

	public int GetLastThreadedProjectileChunkCount()
	{
		return _world.LastThreadedChunkCount;
	}

	public bool WasLastRenderPreparationThreaded()
	{
		return _renderer.WasLastRenderPreparationThreaded();
	}

	public long GetLastRenderPreparationMicroseconds()
	{
		return _renderer.GetLastRenderPreparationMicroseconds();
	}

	public long GetLastThreadedRenderPreparationMicroseconds()
	{
		return _renderer.GetLastThreadedRenderPreparationMicroseconds();
	}

	public int GetLastThreadedRenderWorkerCount()
	{
		return _renderer.GetLastThreadedRenderWorkerCount();
	}

	public int GetLastThreadedRenderChunkCount()
	{
		return _renderer.GetLastThreadedRenderChunkCount();
	}

	public void Clear()
	{
		BindChildrenOrThrow();
		_eventReplayer.ClearProjectiles();
		_targetSync.Clear();
		_world.Clear();
		_eventReplayer.ClearEventsAndListeners();
		_spawnAdapter.ClearChildSpawnDefinitions();
		_renderer.Reset();
	}

	private void ApplyWorldRuntimeSettings()
	{
		_world.MaximumProjectileCount = maximumProjectileListSize;
		_world.MaximumTargetCount = maximumTargetListSize;
		_world.ParallelSimulationEnabled = projectile_threading_enabled;
		_world.MinimumParallelProjectileCount = Math.Max(1, minimum_parallel_projectile_count);
		_world.ParallelChunkSize = Math.Max(1, projectile_parallel_chunk_size);
	}

	private void BindChildrenOrThrow()
	{
		if (_childrenBound)
		{
			return;
		}

		Renderer? renderer = GetNodeOrNull<Renderer>("MultiMeshInstance2D");
		if (renderer == null)
		{
			throw new InvalidOperationException("Projectile root requires a Renderer child named 'MultiMeshInstance2D'.");
		}

		_renderer = renderer;
		_typeRegistry = new ProjectileTypeRegistry(_world, _renderer);
		_eventReplayer = new ProjectileEventReplayer(_world, _renderer, _registry);
		_targetSync = new ProjectileTargetSync(_world, _registry, _typeRegistry);
		_spawnAdapter = new ProjectileSpawnAdapter(_world, _registry, _typeRegistry, _eventReplayer);
		_childrenBound = true;
	}

	private static void RequireConfiguredGroup(StringName groupName, string propertyName)
	{
		if (groupName.ToString() == string.Empty)
		{
			throw new InvalidOperationException(
				$"Projectile root requires '{propertyName}' to be configured in the scene.");
		}
	}
}
