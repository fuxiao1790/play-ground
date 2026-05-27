using Godot;
using System;

/// <summary>
/// Simple SoA projectile column store used by the scoped projectile runtime.
/// </summary>
public sealed class ProjectileStore
{
	public int Count;

	// identity/type/damage
	public EntityHandle[] Entity = Array.Empty<EntityHandle>();
	public int[] TypeId = Array.Empty<int>();
	public int[] TargetMask = Array.Empty<int>();
	public DamageSnapshot[] Damage = Array.Empty<DamageSnapshot>(); // damage is not and should be be calculated in the projectile system, otherwise using a class here would cause pointer chasing in the physics loop

	// transform
	public Vector2[] Position = Array.Empty<Vector2>();
	public Vector2[] Velocity = Array.Empty<Vector2>();
	public float[] Lifetime = Array.Empty<float>();

	// tracking
	public bool[] TrackingEnabled = Array.Empty<bool>();
	public float[] TrackingRangeSquared = Array.Empty<float>();
	public float[] TrackingTurnSpeedRadians = Array.Empty<float>();
	public float[] TrackingQueryCooldownRemaining = Array.Empty<float>();
	public float[] TrackingQueryIntervalSeconds = Array.Empty<float>();
	public EntityHandle[] TrackedTargetEntity = Array.Empty<EntityHandle>();
	public int[] TrackedTargetIndex = Array.Empty<int>();

	// collision
	public bool[] Hit = Array.Empty<bool>();
	public int[] HitTargetIndex = Array.Empty<int>();
	public bool[] PendingDespawn = Array.Empty<bool>();
	public int[] RemainingPierceHits = Array.Empty<int>();
	public Rect2[] WorldBounds = Array.Empty<Rect2>();

	// render
	public int[] RenderSlotIndex = Array.Empty<int>();
	public float[] RenderRotation = Array.Empty<float>();
	public Vector2[] RenderVelocity = Array.Empty<Vector2>();

	// child spawn
	public bool[] CanSpawnChildren = Array.Empty<bool>();
	public int[] ChildSpawnerId = Array.Empty<int>();
	public float[] ChildSpawnIntervalSeconds = Array.Empty<float>();
	public float[] ChildSpawnCooldownRemaining = Array.Empty<float>();
	public int[] ChildSpawnTickIndex = Array.Empty<int>();

	public void EnsureCapacity(int capacity)
	{
		if (Entity.Length >= capacity)
		{
			return;
		}

		int newCapacity = Math.Max(1, Entity.Length);
		while (newCapacity < capacity)
		{
			newCapacity *= 2;
		}

		Array.Resize(ref Entity, newCapacity);
		Array.Resize(ref TypeId, newCapacity);
		Array.Resize(ref TargetMask, newCapacity);
		Array.Resize(ref Damage, newCapacity);
		Array.Resize(ref Position, newCapacity);
		Array.Resize(ref Velocity, newCapacity);
		Array.Resize(ref Lifetime, newCapacity);
		Array.Resize(ref WorldBounds, newCapacity);
		Array.Resize(ref TrackingEnabled, newCapacity);
		Array.Resize(ref TrackingRangeSquared, newCapacity);
		Array.Resize(ref TrackingTurnSpeedRadians, newCapacity);
		Array.Resize(ref TrackingQueryCooldownRemaining, newCapacity);
		Array.Resize(ref TrackingQueryIntervalSeconds, newCapacity);
		Array.Resize(ref TrackedTargetEntity, newCapacity);
		Array.Resize(ref TrackedTargetIndex, newCapacity);
		Array.Resize(ref Hit, newCapacity);
		Array.Resize(ref HitTargetIndex, newCapacity);
		Array.Resize(ref PendingDespawn, newCapacity);
		Array.Resize(ref RemainingPierceHits, newCapacity);
		Array.Resize(ref RenderSlotIndex, newCapacity);
		Array.Resize(ref RenderRotation, newCapacity);
		Array.Resize(ref RenderVelocity, newCapacity);
		Array.Resize(ref CanSpawnChildren, newCapacity);
		Array.Resize(ref ChildSpawnerId, newCapacity);
		Array.Resize(ref ChildSpawnIntervalSeconds, newCapacity);
		Array.Resize(ref ChildSpawnCooldownRemaining, newCapacity);
		Array.Resize(ref ChildSpawnTickIndex, newCapacity);
	}

	public int Append(in ProjectileSpawnCommand command)
	{
		int index = Count;
		EnsureCapacity(index + 1);

		Entity[index] = command.ProjectileHandle;
		TypeId[index] = command.ProjectileTypeId;
		TargetMask[index] = command.TargetMask;
		Damage[index] = command.Damage;
		Position[index] = command.Position;
		Velocity[index] = command.Velocity;
		Lifetime[index] = command.Lifetime;
		WorldBounds[index] = new Rect2(command.Position, Vector2.Zero);
		TrackingEnabled[index] = command.TrackingConfig.Enabled;
		TrackingRangeSquared[index] = command.TrackingConfig.Range * command.TrackingConfig.Range;
		TrackingTurnSpeedRadians[index] = Mathf.DegToRad(command.TrackingConfig.TurnSpeedDegrees);
		TrackingQueryCooldownRemaining[index] = Mathf.Max(0.0f, command.InitialTrackingQueryCooldownSeconds);
		TrackingQueryIntervalSeconds[index] = Mathf.Max(0.0f, command.TrackingQueryIntervalSeconds);
		TrackedTargetEntity[index] = EntityHandle.Invalid;
		TrackedTargetIndex[index] = -1;
		Hit[index] = false;
		HitTargetIndex[index] = -1;
		PendingDespawn[index] = false;
		RemainingPierceHits[index] = Mathf.Max(0, command.ProjectilePierceCount);
		RenderSlotIndex[index] = -1;
		RenderRotation[index] = 0.0f;
		RenderVelocity[index] = Vector2.Zero;
		ChildSpawnerId[index] = command.ChildSpawnerId;
		ChildSpawnIntervalSeconds[index] = Mathf.Max(0.0f, command.ChildSpawnIntervalSeconds);
		CanSpawnChildren[index] = ChildSpawnerId[index] > 0 && ChildSpawnIntervalSeconds[index] > 0.0f;
		ChildSpawnCooldownRemaining[index] = NonNegativeSeconds(ChildSpawnIntervalSeconds[index])
			+ DeterministicOffsetSeconds(
				command.ProjectileHandle,
				command.ChildSpawnerId,
				0,
				command.ChildSpawnIntervalJitterSeconds);
		ChildSpawnTickIndex[index] = 0;

		Count++;
		return index;
	}

	public int AppendFrom(ProjectileStore source, int sourceIndex)
	{
		int index = Count;
		EnsureCapacity(index + 1);
		CopyRow(source, sourceIndex, this, index);
		Count++;
		return index;
	}

	public ProjectileSnapshot Snapshot(int index)
	{
		return new ProjectileSnapshot(
			Entity[index],
			TypeId[index],
			TargetMask[index],
			Damage[index],
			Position[index],
			Velocity[index],
			RenderSlotIndex[index],
			CanSpawnChildren[index],
			ChildSpawnerId[index]);
	}

	public void CopyWithin(int sourceIndex, int destinationIndex)
	{
		if (sourceIndex == destinationIndex)
		{
			return;
		}

		CopyRow(this, sourceIndex, this, destinationIndex);
	}

	public void CopyRangeWithin(int sourceIndex, int destinationIndex, int count)
	{
		if (count <= 0 || sourceIndex == destinationIndex)
		{
			return;
		}

		Array.Copy(Entity, sourceIndex, Entity, destinationIndex, count);
		Array.Copy(TypeId, sourceIndex, TypeId, destinationIndex, count);
		Array.Copy(TargetMask, sourceIndex, TargetMask, destinationIndex, count);
		Array.Copy(Damage, sourceIndex, Damage, destinationIndex, count);
		Array.Copy(Position, sourceIndex, Position, destinationIndex, count);
		Array.Copy(Velocity, sourceIndex, Velocity, destinationIndex, count);
		Array.Copy(Lifetime, sourceIndex, Lifetime, destinationIndex, count);
		Array.Copy(WorldBounds, sourceIndex, WorldBounds, destinationIndex, count);
		Array.Copy(TrackingEnabled, sourceIndex, TrackingEnabled, destinationIndex, count);
		Array.Copy(TrackingRangeSquared, sourceIndex, TrackingRangeSquared, destinationIndex, count);
		Array.Copy(TrackingTurnSpeedRadians, sourceIndex, TrackingTurnSpeedRadians, destinationIndex, count);
		Array.Copy(TrackingQueryCooldownRemaining, sourceIndex, TrackingQueryCooldownRemaining, destinationIndex, count);
		Array.Copy(TrackingQueryIntervalSeconds, sourceIndex, TrackingQueryIntervalSeconds, destinationIndex, count);
		Array.Copy(TrackedTargetEntity, sourceIndex, TrackedTargetEntity, destinationIndex, count);
		Array.Copy(TrackedTargetIndex, sourceIndex, TrackedTargetIndex, destinationIndex, count);
		Array.Copy(Hit, sourceIndex, Hit, destinationIndex, count);
		Array.Copy(HitTargetIndex, sourceIndex, HitTargetIndex, destinationIndex, count);
		Array.Copy(PendingDespawn, sourceIndex, PendingDespawn, destinationIndex, count);
		Array.Copy(RemainingPierceHits, sourceIndex, RemainingPierceHits, destinationIndex, count);
		Array.Copy(RenderSlotIndex, sourceIndex, RenderSlotIndex, destinationIndex, count);
		Array.Copy(RenderRotation, sourceIndex, RenderRotation, destinationIndex, count);
		Array.Copy(RenderVelocity, sourceIndex, RenderVelocity, destinationIndex, count);
		Array.Copy(CanSpawnChildren, sourceIndex, CanSpawnChildren, destinationIndex, count);
		Array.Copy(ChildSpawnerId, sourceIndex, ChildSpawnerId, destinationIndex, count);
		Array.Copy(ChildSpawnIntervalSeconds, sourceIndex, ChildSpawnIntervalSeconds, destinationIndex, count);
		Array.Copy(ChildSpawnCooldownRemaining, sourceIndex, ChildSpawnCooldownRemaining, destinationIndex, count);
		Array.Copy(ChildSpawnTickIndex, sourceIndex, ChildSpawnTickIndex, destinationIndex, count);
	}

	public void RemoveRange(int startIndex, int count)
	{
		if (count <= 0)
		{
			return;
		}

		int endIndex = startIndex + count;
		int tailCount = Count - endIndex;
		for (int i = 0; i < tailCount; i++)
		{
			CopyRow(this, endIndex + i, this, startIndex + i);
		}

		Count -= count;
	}

	public void Clear()
	{
		Count = 0;
	}

	public static float NonNegativeSeconds(float seconds)
	{
		return Mathf.Max(0.0f, seconds);
	}

	public static float DeterministicOffsetSeconds(
		EntityHandle entity,
		int seedA,
		int seedB,
		float maxOffsetSeconds)
	{
		float maxOffset = Mathf.Max(0.0f, maxOffsetSeconds);
		if (maxOffset <= 0.0f)
		{
			return 0.0f;
		}

		return HashToUnit(entity.Id, seedA, seedB) * maxOffset;
	}

	private static void CopyRow(ProjectileStore source, int sourceIndex, ProjectileStore destination, int destinationIndex)
	{
		destination.Entity[destinationIndex] = source.Entity[sourceIndex];
		destination.TypeId[destinationIndex] = source.TypeId[sourceIndex];
		destination.TargetMask[destinationIndex] = source.TargetMask[sourceIndex];
		destination.Damage[destinationIndex] = source.Damage[sourceIndex];
		destination.Position[destinationIndex] = source.Position[sourceIndex];
		destination.Velocity[destinationIndex] = source.Velocity[sourceIndex];
		destination.Lifetime[destinationIndex] = source.Lifetime[sourceIndex];
		destination.WorldBounds[destinationIndex] = source.WorldBounds[sourceIndex];
		destination.TrackingEnabled[destinationIndex] = source.TrackingEnabled[sourceIndex];
		destination.TrackingRangeSquared[destinationIndex] = source.TrackingRangeSquared[sourceIndex];
		destination.TrackingTurnSpeedRadians[destinationIndex] = source.TrackingTurnSpeedRadians[sourceIndex];
		destination.TrackingQueryCooldownRemaining[destinationIndex] = source.TrackingQueryCooldownRemaining[sourceIndex];
		destination.TrackingQueryIntervalSeconds[destinationIndex] = source.TrackingQueryIntervalSeconds[sourceIndex];
		destination.TrackedTargetEntity[destinationIndex] = source.TrackedTargetEntity[sourceIndex];
		destination.TrackedTargetIndex[destinationIndex] = source.TrackedTargetIndex[sourceIndex];
		destination.Hit[destinationIndex] = source.Hit[sourceIndex];
		destination.HitTargetIndex[destinationIndex] = source.HitTargetIndex[sourceIndex];
		destination.PendingDespawn[destinationIndex] = source.PendingDespawn[sourceIndex];
		destination.RemainingPierceHits[destinationIndex] = source.RemainingPierceHits[sourceIndex];
		destination.RenderSlotIndex[destinationIndex] = source.RenderSlotIndex[sourceIndex];
		destination.RenderRotation[destinationIndex] = source.RenderRotation[sourceIndex];
		destination.RenderVelocity[destinationIndex] = source.RenderVelocity[sourceIndex];
		destination.CanSpawnChildren[destinationIndex] = source.CanSpawnChildren[sourceIndex];
		destination.ChildSpawnerId[destinationIndex] = source.ChildSpawnerId[sourceIndex];
		destination.ChildSpawnIntervalSeconds[destinationIndex] = source.ChildSpawnIntervalSeconds[sourceIndex];
		destination.ChildSpawnCooldownRemaining[destinationIndex] = source.ChildSpawnCooldownRemaining[sourceIndex];
		destination.ChildSpawnTickIndex[destinationIndex] = source.ChildSpawnTickIndex[sourceIndex];
	}

	private static float HashToUnit(int entityId, int childSpawnerId, int tickIndex)
	{
		uint hash = (uint)entityId;
		hash ^= (uint)childSpawnerId * 0x9E3779B9u;
		hash ^= (uint)tickIndex * 0x85EBCA6Bu;
		hash ^= hash >> 16;
		hash *= 0x7FEB352Du;
		hash ^= hash >> 15;
		hash *= 0x846CA68Bu;
		hash ^= hash >> 16;
		return ((hash & 0x00FFFFFFu) + 1u) / 16777217.0f;
	}
}

/// <summary>
/// Simple SoA target snapshot store used by projectile tracking and collision.
/// </summary>
public sealed class TargetStore
{
	public int Count;

	public EntityHandle[] Entity = Array.Empty<EntityHandle>();
	public int[] TypeId = Array.Empty<int>();
	public int[] CollisionLayer = Array.Empty<int>();
	public Vector2[] Position = Array.Empty<Vector2>();
	public Rect2[] WorldBounds = Array.Empty<Rect2>();

	public void EnsureCapacity(int capacity)
	{
		if (Entity.Length >= capacity)
		{
			return;
		}

		int newCapacity = Math.Max(1, Entity.Length);
		while (newCapacity < capacity)
		{
			newCapacity *= 2;
		}

		Array.Resize(ref Entity, newCapacity);
		Array.Resize(ref TypeId, newCapacity);
		Array.Resize(ref CollisionLayer, newCapacity);
		Array.Resize(ref Position, newCapacity);
		Array.Resize(ref WorldBounds, newCapacity);
	}

	public void Add(EntityHandle entity, int typeId, int collisionLayer, Vector2 position)
	{
		int index = Count;
		EnsureCapacity(index + 1);
		Entity[index] = entity;
		TypeId[index] = typeId;
		CollisionLayer[index] = collisionLayer;
		Position[index] = position;
		WorldBounds[index] = new Rect2(position, Vector2.Zero);
		Count++;
	}

	public void Clear()
	{
		Count = 0;
	}
}

/// <summary>
/// Stable copy of projectile data needed after SoA rows may compact or move.
/// </summary>
public readonly struct ProjectileSnapshot
{
	public readonly EntityHandle Entity;
	public readonly int TypeId;
	public readonly int TargetMask;
	public readonly DamageSnapshot Damage;
	public readonly Vector2 Position;
	public readonly Vector2 Velocity;
	public readonly int RenderSlotIndex;
	public readonly bool CanSpawnChildren;
	public readonly int ChildSpawnerId;

	public ProjectileSnapshot(
		EntityHandle entity,
		int typeId,
		int targetMask,
		DamageSnapshot damage,
		Vector2 position,
		Vector2 velocity,
		int renderSlotIndex,
		bool canSpawnChildren,
		int childSpawnerId)
	{
		Entity = entity;
		TypeId = typeId;
		TargetMask = targetMask;
		Damage = damage;
		Position = position;
		Velocity = velocity;
		RenderSlotIndex = renderSlotIndex;
		CanSpawnChildren = canSpawnChildren;
		ChildSpawnerId = childSpawnerId;
	}
}
