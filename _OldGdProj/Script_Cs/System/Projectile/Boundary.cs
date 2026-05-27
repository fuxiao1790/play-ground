using Godot;

/// <summary>
/// Godot-facing projectile spawn request submitted to a scoped projectile root.
/// </summary>
public readonly struct ProjectileSpawnRequest
{
	public readonly Node? Source;
	public readonly int ProjectileTypeId;
	public readonly Vector2 Position;
	public readonly Vector2 Velocity;
	public readonly float Lifetime;
	public readonly int TargetMask;
	public readonly Projectile? HitListener;
	public readonly ProjectileTrackingConfig TrackingConfig;
	public readonly DamageSnapshot Damage;
	public readonly int ChildSpawnerId;
	public readonly float ChildSpawnIntervalSeconds;
	public readonly float ChildSpawnIntervalJitterSeconds;
	public readonly int ProjectilePierceCount;

	public ProjectileSpawnRequest(
		Node source,
		int projectileTypeId,
		Vector2 position,
		Vector2 velocity,
		float lifetime,
		int targetMask,
		Projectile? hitListener = null,
		ProjectileTrackingConfig trackingConfig = default,
		DamageSnapshot? damage = null,
		int childSpawnerId = 0,
		float childSpawnIntervalSeconds = 0.0f,
		float childSpawnIntervalJitterSeconds = 0.0f,
		int projectilePierceCount = 0)
	{
		Source = source;
		ProjectileTypeId = projectileTypeId;
		Position = position;
		Velocity = velocity;
		Lifetime = lifetime;
		TargetMask = targetMask;
		HitListener = hitListener;
		TrackingConfig = trackingConfig;
		Damage = damage ?? DamageSnapshot.Empty;
		ChildSpawnerId = childSpawnerId;
		ChildSpawnIntervalSeconds = childSpawnIntervalSeconds;
		ChildSpawnIntervalJitterSeconds = childSpawnIntervalJitterSeconds;
		ProjectilePierceCount = Mathf.Max(0, projectilePierceCount);
	}
}

/// <summary>
/// Plain spawn request submitted by the Godot adapter into ProjectileWorld.
/// </summary>
public readonly struct ProjectileSpawnCommand
{
	public readonly EntityHandle ProjectileHandle;
	public readonly int ProjectileTypeId;
	public readonly Vector2 Position;
	public readonly Vector2 Velocity;
	public readonly float Lifetime;
	public readonly int TargetMask;
	public readonly DamageSnapshot Damage;
	public readonly ProjectileTrackingConfig TrackingConfig;
	public readonly float InitialTrackingQueryCooldownSeconds;
	public readonly float TrackingQueryIntervalSeconds;
	public readonly int StableSpawnId;
	public readonly int ProjectilePierceCount;
	public readonly int ChildSpawnerId;
	public readonly float ChildSpawnIntervalSeconds;
	public readonly float ChildSpawnIntervalJitterSeconds;

	public ProjectileSpawnCommand(
		EntityHandle projectileHandle,
		int projectileTypeId,
		Vector2 position,
		Vector2 velocity,
		float lifetime,
		int targetMask,
		DamageSnapshot? damage,
		in ProjectileTrackingConfig trackingConfig,
		float initialTrackingQueryCooldownSeconds,
		float trackingQueryIntervalSeconds,
		int stableSpawnId = 0,
		int projectilePierceCount = 0,
		int childSpawnerId = 0,
		float childSpawnIntervalSeconds = 0.0f,
		float childSpawnIntervalJitterSeconds = 0.0f)
	{
		ProjectileHandle = projectileHandle;
		ProjectileTypeId = projectileTypeId;
		Position = position;
		Velocity = velocity;
		Lifetime = lifetime;
		TargetMask = targetMask;
		Damage = damage ?? DamageSnapshot.Empty;
		TrackingConfig = trackingConfig;
		InitialTrackingQueryCooldownSeconds = initialTrackingQueryCooldownSeconds;
		TrackingQueryIntervalSeconds = trackingQueryIntervalSeconds;
		ProjectilePierceCount = Mathf.Max(0, projectilePierceCount);
		StableSpawnId = stableSpawnId;
		ChildSpawnerId = childSpawnerId;
		ChildSpawnIntervalSeconds = childSpawnIntervalSeconds;
		ChildSpawnIntervalJitterSeconds = childSpawnIntervalJitterSeconds;
	}
}

/// <summary>
/// Plain target state snapshot submitted by Root for the current physics tick.
/// </summary>
public readonly struct TargetSnapshot
{
	public readonly EntityHandle TargetHandle;
	public readonly int TargetTypeId;
	public readonly int CollisionLayer;
	public readonly Vector2 Position;

	public TargetSnapshot(
		EntityHandle targetHandle,
		int targetTypeId,
		int collisionLayer,
		Vector2 position)
	{
		TargetHandle = targetHandle;
		TargetTypeId = targetTypeId;
		CollisionLayer = collisionLayer;
		Position = position;
	}
}

/// <summary>
/// Event raised when a pending projectile becomes active in the world.
/// </summary>
public readonly struct ProjectileSpawnedEvent
{
	public readonly EntityHandle Projectile;
	public readonly int ActiveIndex;

	public ProjectileSpawnedEvent(EntityHandle projectile, int activeIndex)
	{
		Projectile = projectile;
		ActiveIndex = activeIndex;
	}
}

/// <summary>
/// Event raised when an active projectile leaves the world.
/// </summary>
public readonly struct ProjectileDespawnedEvent
{
	public readonly EntityHandle Projectile;
	public readonly ProjectileSnapshot Data;

	public ProjectileDespawnedEvent(EntityHandle projectile, in ProjectileSnapshot data)
	{
		Projectile = projectile;
		Data = data;
	}
}

/// <summary>
/// Event raised when a target handle disappears from submitted target snapshots.
/// </summary>
public readonly struct TargetDespawnedEvent
{
	public readonly EntityHandle Target;

	public TargetDespawnedEvent(EntityHandle target)
	{
		Target = target;
	}
}

/// <summary>
/// Event raised by the pure runtime when an original projectile reaches a child-spawn interval.
/// </summary>
public readonly struct ChildProjectileSpawnRequestedEvent
{
	public readonly EntityHandle Parent;
	public readonly int ChildSpawnerId;
	public readonly Vector2 Position;
	public readonly Vector2 Velocity;
	public readonly int TickIndex;

	public ChildProjectileSpawnRequestedEvent(
		EntityHandle parent,
		int childSpawnerId,
		Vector2 position,
		Vector2 velocity,
		int tickIndex)
	{
		Parent = parent;
		ChildSpawnerId = childSpawnerId;
		Position = position;
		Velocity = velocity;
		TickIndex = tickIndex;
	}
}
