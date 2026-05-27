using Godot;

/// <summary>
/// Godot-facing AOE spawn request submitted to a scoped AOE root.
/// </summary>
public readonly struct AoeSpawnRequest
{
	public readonly Node? Source;
	public readonly int AoeTypeId;
	public readonly Vector2 Position;
	public readonly int TargetMask;
	public readonly DamageSnapshot Damage;
	public readonly float LifetimeSeconds;
	public readonly float TickIntervalSeconds;
	public readonly Aoe? HitListener;

	public AoeSpawnRequest(
		Node source,
		int aoeTypeId,
		Vector2 position,
		int targetMask,
		DamageSnapshot? damage = null,
		float lifetimeSeconds = 0.0f,
		float tickIntervalSeconds = 0.0f,
		Aoe? hitListener = null)
	{
		Source = source;
		AoeTypeId = aoeTypeId;
		Position = position;
		TargetMask = targetMask;
		Damage = damage ?? DamageSnapshot.Empty;
		LifetimeSeconds = Mathf.Max(0.0f, lifetimeSeconds);
		TickIntervalSeconds = Mathf.Max(0.0f, tickIntervalSeconds);
		HitListener = hitListener;
	}
}

/// <summary>
/// Plain spawn command owned by the AOE runtime core.
/// </summary>
public readonly struct AoeSpawnCommand
{
	public readonly EntityHandle AoeHandle;
	public readonly int AoeTypeId;
	public readonly Vector2 Position;
	public readonly int TargetMask;
	public readonly DamageSnapshot Damage;
	public readonly float LifetimeSeconds;
	public readonly float TickIntervalSeconds;

	public AoeSpawnCommand(
		EntityHandle aoeHandle,
		int aoeTypeId,
		Vector2 position,
		int targetMask,
		DamageSnapshot? damage,
		float lifetimeSeconds,
		float tickIntervalSeconds)
	{
		AoeHandle = aoeHandle;
		AoeTypeId = aoeTypeId;
		Position = position;
		TargetMask = targetMask;
		Damage = damage ?? DamageSnapshot.Empty;
		LifetimeSeconds = Mathf.Max(0.0f, lifetimeSeconds);
		TickIntervalSeconds = Mathf.Max(0.0f, tickIntervalSeconds);
	}
}

/// <summary>
/// Gameplay callback payload built when replaying an AOE hit event.
/// </summary>
public readonly struct AoeHitContext
{
	public readonly EntityHandle Source;
	public readonly EntityHandle Target;
	public readonly int AoeTypeId;
	public readonly Node2D TargetNode;
	public readonly Vector2 Position;
	public readonly DamageSnapshot Damage;

	public AoeHitContext(
		EntityHandle source,
		EntityHandle target,
		int aoeTypeId,
		Node2D targetNode,
		Vector2 position,
		DamageSnapshot? damage = null)
	{
		Source = source;
		Target = target;
		AoeTypeId = aoeTypeId;
		TargetNode = targetNode;
		Position = position;
		Damage = damage ?? DamageSnapshot.Empty;
	}
}

/// <summary>
/// Plain hit event emitted by the AOE world and replayed by the adapter.
/// </summary>
public readonly struct AoeHitEvent
{
	public readonly EntityHandle Source;
	public readonly EntityHandle Target;
	public readonly int AoeTypeId;
	public readonly Vector2 Position;
	public readonly DamageSnapshot Damage;

	public AoeHitEvent(
		EntityHandle source,
		EntityHandle target,
		int aoeTypeId,
		Vector2 position,
		DamageSnapshot damage)
	{
		Source = source;
		Target = target;
		AoeTypeId = aoeTypeId;
		Position = position;
		Damage = damage;
	}
}

/// <summary>
/// Event raised when an AOE leaves the runtime.
/// </summary>
public readonly struct AoeDespawnedEvent
{
	public readonly EntityHandle Source;

	public AoeDespawnedEvent(EntityHandle source)
	{
		Source = source;
	}
}
