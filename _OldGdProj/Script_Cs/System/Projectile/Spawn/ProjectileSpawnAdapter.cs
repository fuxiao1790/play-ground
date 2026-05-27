using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Converts public Godot spawn calls into plain ProjectileWorld spawn commands.
/// </summary>
internal sealed class ProjectileSpawnAdapter
{
	private readonly ProjectileWorld _world;
	private readonly EntityRegistry _registry;
	private readonly ProjectileTypeRegistry _typeRegistry;
	private readonly ProjectileEventReplayer _eventReplayer;
	private readonly RandomNumberGenerator _rng = new();
	private readonly Dictionary<int, ChildProjectileSpawnDefinition> _childSpawnDefinitions = new();
	private readonly List<ProjectileVolleyBuilder.SpawnRequest> _childSpawnRequests = new();
	private int _nextChildSpawnDefinitionId = 1;

	public ProjectileSpawnAdapter(
		ProjectileWorld world,
		EntityRegistry registry,
		ProjectileTypeRegistry typeRegistry,
		ProjectileEventReplayer eventReplayer)
	{
		_world = world;
		_registry = registry;
		_typeRegistry = typeRegistry;
		_eventReplayer = eventReplayer;
	}

	public void Randomize()
	{
		_rng.Randomize();
	}

	public void SpawnProjectile(
		in ProjectileSpawnRequest request,
		float trackingQueryIntervalSeconds,
		float trackingQueryIntervalSpawnJitterSeconds)
	{
		if (request.Source == null)
		{
			throw new InvalidOperationException("Projectile spawn request requires a source node.");
		}

		_typeRegistry.ValidateRegisteredProjectileType(request.ProjectileTypeId);

		EntityHandle handle = _registry.RegisterProjectile(new GameplayProjectile(_eventReplayer));
		_eventReplayer.RegisterHitListener(handle, request.HitListener);

		ProjectileSpawnCommand command = new(
			handle,
			request.ProjectileTypeId,
			request.Position,
			request.Velocity,
			request.Lifetime,
			request.TargetMask,
			request.Damage,
			request.TrackingConfig,
			InitialTrackingQueryCooldownSeconds(
				request.TrackingConfig,
				trackingQueryIntervalSeconds,
				trackingQueryIntervalSpawnJitterSeconds),
			TrackingQueryIntervalSeconds(trackingQueryIntervalSeconds),
			0,
			request.ProjectilePierceCount,
			request.ChildSpawnerId,
			request.ChildSpawnIntervalSeconds,
			request.ChildSpawnIntervalJitterSeconds);
		_world.SubmitSpawn(in command);
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
		float trackingQueryIntervalSeconds,
		float trackingQueryIntervalSpawnJitterSeconds,
		int projectilePierceCount)
	{
		_typeRegistry.ValidateRegisteredProjectileType(projectileTypeId);
		if (source == null)
		{
			throw new InvalidOperationException("Child projectile spawn definition requires a source node.");
		}

		int id = _nextChildSpawnDefinitionId++;
		_childSpawnDefinitions.Add(
			id,
			new ChildProjectileSpawnDefinition(
				source,
				projectileTypeId,
				Mathf.Max(1, childCount),
				Mathf.Max(0.0f, speed),
				Mathf.Max(0.01f, lifetime),
				targetMask,
				hitListener,
				trackingConfig,
				damage ?? DamageSnapshot.Empty,
				pattern,
				Mathf.Max(1, parentProjectileCount),
				TrackingQueryIntervalSeconds(trackingQueryIntervalSeconds),
				Mathf.Max(0.0f, trackingQueryIntervalSpawnJitterSeconds),
				Mathf.Max(0, projectilePierceCount)));
		return id;
	}

	public void SpawnChildrenFromRequest(in ChildProjectileSpawnRequestedEvent request)
	{
		if (!_childSpawnDefinitions.TryGetValue(request.ChildSpawnerId, out ChildProjectileSpawnDefinition? definition))
		{
			return;
		}

		if (!GodotObject.IsInstanceValid(definition.Source))
		{
			return;
		}

		definition.Pattern.Build(
			_childSpawnRequests,
			request.Position,
			request.Velocity,
			definition.Count,
			definition.Speed,
			request.TickIndex);

		for (int i = 0; i < _childSpawnRequests.Count; i++)
		{
			ProjectileVolleyBuilder.SpawnRequest child = _childSpawnRequests[i];
			SpawnProjectile(
				new ProjectileSpawnRequest(
					definition.Source,
					definition.ProjectileTypeId,
					child.Position,
					child.Velocity,
					definition.Lifetime,
					definition.TargetMask,
					definition.HitListener,
					definition.TrackingConfig,
					definition.Damage,
					projectilePierceCount: definition.ProjectilePierceCount),
				definition.TrackingQueryIntervalSeconds,
				definition.TrackingQueryIntervalSpawnJitterSeconds);
		}
	}

	public void ReleaseChildSpawnDefinition(in ProjectileSnapshot data)
	{
		if (!data.CanSpawnChildren)
		{
			return;
		}

		if (!_childSpawnDefinitions.TryGetValue(data.ChildSpawnerId, out ChildProjectileSpawnDefinition? definition))
		{
			return;
		}

		definition.LiveParentCount--;
		if (definition.LiveParentCount <= 0)
		{
			_childSpawnDefinitions.Remove(data.ChildSpawnerId);
		}
	}

	public void ClearChildSpawnDefinitions()
	{
		_childSpawnDefinitions.Clear();
		_childSpawnRequests.Clear();
	}

	private float InitialTrackingQueryCooldownSeconds(
		in ProjectileTrackingConfig trackingConfig,
		float trackingQueryIntervalSeconds,
		float trackingQueryIntervalSpawnJitterSeconds)
	{
		if (!trackingConfig.Enabled)
		{
			return 0.0f;
		}

		float baseInterval = TrackingQueryIntervalSeconds(trackingQueryIntervalSeconds);
		float jitterRange = Mathf.Max(0.0f, trackingQueryIntervalSpawnJitterSeconds);
		if (jitterRange <= 0.0f)
		{
			return baseInterval;
		}

		return baseInterval + _rng.RandfRange(0.0f, jitterRange);
	}

	private static float TrackingQueryIntervalSeconds(float trackingQueryIntervalSeconds)
	{
		return Mathf.Max(0.0f, trackingQueryIntervalSeconds);
	}

	private sealed class ChildProjectileSpawnDefinition
	{
		public readonly Node Source;
		public readonly int ProjectileTypeId;
		public readonly int Count;
		public readonly float Speed;
		public readonly float Lifetime;
		public readonly int TargetMask;
		public readonly Projectile? HitListener;
		public readonly ProjectileTrackingConfig TrackingConfig;
		public readonly DamageSnapshot Damage;
		public readonly ProjectileChildSpawnPattern Pattern;
		public readonly float TrackingQueryIntervalSeconds;
		public readonly float TrackingQueryIntervalSpawnJitterSeconds;
		public readonly int ProjectilePierceCount;
		public int LiveParentCount;

		public ChildProjectileSpawnDefinition(
			Node source,
			int projectileTypeId,
			int count,
			float speed,
			float lifetime,
			int targetMask,
			Projectile? hitListener,
			in ProjectileTrackingConfig trackingConfig,
			DamageSnapshot damage,
			ProjectileChildSpawnPattern pattern,
			int liveParentCount,
			float trackingQueryIntervalSeconds,
			float trackingQueryIntervalSpawnJitterSeconds,
			int projectilePierceCount)
		{
			Source = source;
			ProjectileTypeId = projectileTypeId;
			Count = count;
			Speed = speed;
			Lifetime = lifetime;
			TargetMask = targetMask;
			HitListener = hitListener;
			TrackingConfig = trackingConfig;
			Damage = damage;
			Pattern = pattern;
			LiveParentCount = liveParentCount;
			TrackingQueryIntervalSeconds = trackingQueryIntervalSeconds;
			TrackingQueryIntervalSpawnJitterSeconds = trackingQueryIntervalSpawnJitterSeconds;
			ProjectilePierceCount = projectilePierceCount;
		}
	}
}
