using Godot;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

/// <summary>
/// Pure projectile runtime core for one scoped projectile root.
/// Owns SoA projectile and target data, spawn promotion, movement, tracking,
/// collision, despawn queues, and plain event buffers.
/// Godot node lookup, scene baking, rendering, and gameplay callback replay
/// belong in Root or other adapter-side code.
/// </summary>
public sealed class ProjectileWorld
{
	private readonly Movement _movement = new();
	private readonly Tracking _tracking = new();
	private readonly Collision _collision;
	private readonly IProjectileWorkScheduler _serialScheduler = new SerialProjectileWorkScheduler();
	private readonly IProjectileWorkScheduler _parallelScheduler = new TaskProjectileWorkScheduler();
	private readonly ProjectileStore _projectiles = new();
	private readonly ProjectileStore _pendingProjectileSpawn = new();
	private readonly TargetStore _targets = new();
	private readonly HashSet<EntityHandle> _submittedTargetHandles = new();
	private readonly List<ProjectileSpawnedEvent> _spawnedEvents = new(1);
	private readonly List<ProjectileDespawnedEvent> _despawnedEvents = new(1);
	private readonly List<TargetDespawnedEvent> _targetDespawnedEvents = new(1);
	private readonly List<HitEvent> _hitEvents = new(1);
	private readonly List<ChildProjectileSpawnRequestedEvent> _childSpawnRequestedEvents = new(1);
	private readonly List<ProjectileRangeEvents> _rangeEvents = new(1);
	private readonly Dictionary<EntityHandle, HashSet<EntityHandle>> _contactGates = new();
	private readonly List<EntityHandle> _releasedContactGates = new(1);
	private readonly object _rangeEventsLock = new();

	private bool _hasPendingProjectileDespawns;
	private long _lastTrackingStepMicroseconds;
	private long _totalTrackingMicroseconds;
	private long _lastStepMicroseconds;
	private long _lastThreadedStepMicroseconds;
	private int _lastTrackingProjectileCount;
	private int _lastThreadedWorkerCount;
	private int _lastThreadedChunkCount;
	private bool _trackingTimingEnabled;
	private bool _lastStepUsedThreading;

	public int MaximumProjectileCount { get; set; } = 100000;
	public int MaximumTargetCount { get; set; } = 100;
	public bool ParallelSimulationEnabled { get; set; }
	public int MinimumParallelProjectileCount { get; set; } = 2048;
	public int ParallelChunkSize { get; set; } = 512;
	public int ActiveCount => _projectiles.Count + _pendingProjectileSpawn.Count;
	public long LastTrackingStepMicroseconds => _lastTrackingStepMicroseconds;
	public long TotalTrackingMicroseconds => _totalTrackingMicroseconds;
	public long LastStepMicroseconds => _lastStepMicroseconds;
	public long LastThreadedStepMicroseconds => _lastThreadedStepMicroseconds;
	public int LastTrackingProjectileCount => _lastTrackingProjectileCount;
	public int LastThreadedWorkerCount => _lastThreadedWorkerCount;
	public int LastThreadedChunkCount => _lastThreadedChunkCount;
	public bool LastStepUsedThreading => _lastStepUsedThreading;
	public ProjectileStore ActiveProjectiles => _projectiles;
	public ProjectileStore PendingProjectiles => _pendingProjectileSpawn;

	public ProjectileWorld(params Collision.BroadPhaseProjectileFilterChain[] collisionFilters)
	{
		_collision = new Collision(collisionFilters);
	}

	public void RegisterProjectileType(ProjectileDefinition definition)
	{
		_collision.RegisterProjectileDefinition(definition);
	}

	public void RegisterTargetType(TargetDefinition definition)
	{
		_collision.RegisterTargetDefinition(definition);
	}

	public void SubmitTargets(IReadOnlyList<TargetSnapshot> snapshots)
	{
		_submittedTargetHandles.Clear();
		int targetCount = Mathf.Min(snapshots.Count, MaximumTargetCount);
		for (int i = 0; i < targetCount; i++)
		{
			_submittedTargetHandles.Add(snapshots[i].TargetHandle);
		}

		for (int i = 0; i < _targets.Count; i++)
		{
			if (!_submittedTargetHandles.Contains(_targets.Entity[i]))
			{
				_targetDespawnedEvents.Add(new TargetDespawnedEvent(_targets.Entity[i]));
			}
		}

		_targets.Clear();
		_targets.EnsureCapacity(targetCount);
		for (int i = 0; i < targetCount; i++)
		{
			TargetSnapshot snapshot = snapshots[i];
			_targets.Add(
				snapshot.TargetHandle,
				snapshot.TargetTypeId,
				snapshot.CollisionLayer,
				snapshot.Position);
		}
	}

	public void SubmitSpawn(in ProjectileSpawnCommand command)
	{
		_pendingProjectileSpawn.Append(in command);
	}

	public void Step(double delta)
	{
		long stepStartTicks = Stopwatch.GetTimestamp();
		_lastTrackingStepMicroseconds = 0;
		FlushProjectileDespawns();
		PromotePendingProjectiles();
		_collision.BuildTargets(_targets);

		int startIndex = 0;
		int endIndex = _projectiles.Count;
		int rangeCount = endIndex - startIndex;
		_lastTrackingProjectileCount = endIndex;
		if (endIndex <= 0)
		{
			SetStepTiming(stepStartTicks, false, 0);
			return;
		}

		bool useThreading = ShouldUseThreading(rangeCount);
		IProjectileWorkScheduler scheduler = SchedulerFor(useThreading);
		int chunkSize = SanitizedChunkSize();
		int threadedChunkCount = useThreading
			? TaskProjectileWorkScheduler.ChunkCount(rangeCount, chunkSize)
			: 0;
		if (_trackingTimingEnabled)
		{
			long trackingStartTicks = Stopwatch.GetTimestamp();
			ForEachProjectileRange(scheduler, rangeCount, chunkSize, startIndex, (rangeStart, rangeEnd) =>
				_tracking.Apply(_projectiles, _targets, rangeStart, rangeEnd, delta));
			_lastTrackingStepMicroseconds =
				((Stopwatch.GetTimestamp() - trackingStartTicks) * 1000000L) / Stopwatch.Frequency;
			_totalTrackingMicroseconds += _lastTrackingStepMicroseconds;
		}
		else
		{
			ForEachProjectileRange(scheduler, rangeCount, chunkSize, startIndex, (rangeStart, rangeEnd) =>
				_tracking.Apply(_projectiles, _targets, rangeStart, rangeEnd, delta));
		}

		ForEachProjectileRange(scheduler, rangeCount, chunkSize, startIndex, (rangeStart, rangeEnd) =>
			_movement.Apply(_projectiles, rangeStart, rangeEnd, delta));

		int hasExpiredDespawns = 0;
		ForEachProjectileRange(scheduler, rangeCount, chunkSize, startIndex, (rangeStart, rangeEnd) =>
		{
			if (MarkExpiredProjectilesRange(rangeStart, rangeEnd))
			{
				Interlocked.Exchange(ref hasExpiredDespawns, 1);
			}
		});
		if (hasExpiredDespawns != 0)
		{
			_hasPendingProjectileDespawns = true;
		}

		ForEachProjectileRange(scheduler, rangeCount, chunkSize, startIndex, (rangeStart, rangeEnd) =>
			_collision.UpdateProjectileBounds(_projectiles, rangeStart, rangeEnd));
		_collision.ReleaseExitedContactGates(
			_projectiles,
			_targets,
			_contactGates,
			_releasedContactGates,
			startIndex,
			endIndex);
		ForEachProjectileRange(scheduler, rangeCount, chunkSize, startIndex, (rangeStart, rangeEnd) =>
			_collision.Apply(
				_projectiles,
				_targets,
				_contactGates,
				rangeStart,
				rangeEnd));
		CollectHitAndChildSpawnEvents(scheduler, rangeCount, chunkSize, startIndex, delta);
		SetStepTiming(stepStartTicks, useThreading, threadedChunkCount);
	}

	public void DrainEvents(
		List<ProjectileSpawnedEvent> spawnedEvents,
		List<ProjectileDespawnedEvent> despawnedEvents,
		List<TargetDespawnedEvent> targetDespawnedEvents,
		List<HitEvent> hitEvents,
		List<ChildProjectileSpawnRequestedEvent> childSpawnRequestedEvents)
	{
		Drain(_spawnedEvents, spawnedEvents);
		Drain(_despawnedEvents, despawnedEvents);
		Drain(_targetDespawnedEvents, targetDespawnedEvents);
		Drain(_hitEvents, hitEvents);
		Drain(_childSpawnRequestedEvents, childSpawnRequestedEvents);
	}

	public bool TryGetProjectile(EntityHandle handle, out ProjectileSnapshot data)
	{
		for (int i = 0; i < _projectiles.Count; i++)
		{
			if (_projectiles.Entity[i] == handle)
			{
				data = _projectiles.Snapshot(i);
				return true;
			}
		}

		for (int i = 0; i < _pendingProjectileSpawn.Count; i++)
		{
			if (_pendingProjectileSpawn.Entity[i] == handle)
			{
				data = _pendingProjectileSpawn.Snapshot(i);
				return true;
			}
		}

		data = default;
		return false;
	}

	public bool TryGetActiveProjectileIndex(EntityHandle handle, int activeIndex, out int projectileIndex)
	{
		if (activeIndex >= 0
			&& activeIndex < _projectiles.Count
			&& _projectiles.Entity[activeIndex] == handle)
		{
			projectileIndex = activeIndex;
			return true;
		}

		for (int i = 0; i < _projectiles.Count; i++)
		{
			if (_projectiles.Entity[i] == handle)
			{
				projectileIndex = i;
				return true;
			}
		}

		projectileIndex = -1;
		return false;
	}

	public void SetTrackingTimingEnabled(bool enabled)
	{
		_trackingTimingEnabled = enabled;
		if (!enabled)
		{
			_lastTrackingStepMicroseconds = 0;
		}
	}

	public void Clear()
	{
		_projectiles.Clear();
		_pendingProjectileSpawn.Clear();
		_targets.Clear();
		_submittedTargetHandles.Clear();
		_spawnedEvents.Clear();
		_despawnedEvents.Clear();
		_targetDespawnedEvents.Clear();
		_hitEvents.Clear();
		_childSpawnRequestedEvents.Clear();
		_rangeEvents.Clear();
		_contactGates.Clear();
		_releasedContactGates.Clear();
		_lastTrackingStepMicroseconds = 0;
		_totalTrackingMicroseconds = 0;
		_lastTrackingProjectileCount = 0;
		_lastStepMicroseconds = 0;
		_lastThreadedStepMicroseconds = 0;
		_lastThreadedWorkerCount = 0;
		_lastThreadedChunkCount = 0;
		_lastStepUsedThreading = false;
		_hasPendingProjectileDespawns = false;
	}

	private bool MarkExpiredProjectilesRange(int startIndex, int endIndex)
	{
		bool hasExpiredDespawns = false;
		for (int i = startIndex; i < endIndex; i++)
		{
			if (_projectiles.Lifetime[i] > 0.0f)
			{
				continue;
			}

			_projectiles.PendingDespawn[i] = true;
			hasExpiredDespawns = true;
		}

		return hasExpiredDespawns;
	}

	private void CollectHitAndChildSpawnEvents(
		IProjectileWorkScheduler scheduler,
		int count,
		int chunkSize,
		int offset,
		double delta)
	{
		_rangeEvents.Clear();
		ForEachProjectileRange(scheduler, count, chunkSize, offset, (rangeStart, rangeEnd) =>
		{
			ProjectileRangeEvents events = new(rangeStart);
			ResolveHitsAndChildSpawnsRange(rangeStart, rangeEnd, delta, events);
			AddRangeEvents(events);
		});

		_rangeEvents.Sort(static (left, right) => left.StartIndex.CompareTo(right.StartIndex));
		for (int i = 0; i < _rangeEvents.Count; i++)
		{
			MergeRangeEvents(_rangeEvents[i]);
		}

		_rangeEvents.Clear();
	}

	private void ResolveHitsAndChildSpawnsRange(
		int startIndex,
		int endIndex,
		double delta,
		ProjectileRangeEvents events)
	{
		for (int i = startIndex; i < endIndex; i++)
		{
			if (_projectiles.PendingDespawn[i])
			{
				continue;
			}

			int hitTargetIndex = _projectiles.HitTargetIndex[i];
			if (!_projectiles.Hit[i] || hitTargetIndex < 0 || hitTargetIndex >= _targets.Count)
			{
				QueueChildSpawnRequests(i, delta, events);
				continue;
			}

			events.HitEvents.Add(new HitEvent(
				_projectiles.Entity[i],
				_targets.Entity[hitTargetIndex],
				_projectiles.TypeId[i],
				_projectiles.Position[i],
				_projectiles.Damage[i]));

			if (_projectiles.RemainingPierceHits[i] <= 0)
			{
				_projectiles.PendingDespawn[i] = true;
				events.HasPendingProjectileDespawns = true;
				continue;
			}

			_projectiles.RemainingPierceHits[i]--;
			events.ContactGates.Add(new ContactGateEvent(
				_projectiles.Entity[i],
				_targets.Entity[hitTargetIndex]));
			QueueChildSpawnRequests(i, delta, events);
		}
	}

	private void QueueChildSpawnRequests(int projectileIndex, double delta, ProjectileRangeEvents events)
	{
		if (!_projectiles.CanSpawnChildren[projectileIndex])
		{
			return;
		}

		_projectiles.ChildSpawnCooldownRemaining[projectileIndex] -= (float)delta;
		while (_projectiles.ChildSpawnCooldownRemaining[projectileIndex] <= 0.0f)
		{
			_projectiles.ChildSpawnTickIndex[projectileIndex]++;
			events.ChildSpawnRequestedEvents.Add(new ChildProjectileSpawnRequestedEvent(
				_projectiles.Entity[projectileIndex],
				_projectiles.ChildSpawnerId[projectileIndex],
				_projectiles.Position[projectileIndex],
				_projectiles.Velocity[projectileIndex],
				_projectiles.ChildSpawnTickIndex[projectileIndex]));
			_projectiles.ChildSpawnCooldownRemaining[projectileIndex] += ProjectileStore.NonNegativeSeconds(
				_projectiles.ChildSpawnIntervalSeconds[projectileIndex]);
		}
	}

	private void AddRangeEvents(ProjectileRangeEvents events)
	{
		lock (_rangeEventsLock)
		{
			_rangeEvents.Add(events);
		}
	}

	private void MergeRangeEvents(ProjectileRangeEvents events)
	{
		if (events.HasPendingProjectileDespawns)
		{
			_hasPendingProjectileDespawns = true;
		}

		for (int i = 0; i < events.HitEvents.Count; i++)
		{
			_hitEvents.Add(events.HitEvents[i]);
		}

		for (int i = 0; i < events.ContactGates.Count; i++)
		{
			ContactGateEvent gateEvent = events.ContactGates[i];
			GateContact(gateEvent.Projectile, gateEvent.Target);
		}

		for (int i = 0; i < events.ChildSpawnRequestedEvents.Count; i++)
		{
			_childSpawnRequestedEvents.Add(events.ChildSpawnRequestedEvents[i]);
		}
	}

	private void PromotePendingProjectiles()
	{
		if (_pendingProjectileSpawn.Count == 0)
		{
			return;
		}

		int availableSlots = MaximumProjectileCount - _projectiles.Count;
		if (availableSlots <= 0)
		{
			_pendingProjectileSpawn.Clear();
			return;
		}

		int spawnCount = Mathf.Min(_pendingProjectileSpawn.Count, availableSlots);
		_projectiles.EnsureCapacity(_projectiles.Count + spawnCount);
		for (int i = 0; i < spawnCount; i++)
		{
			int activeIndex = _projectiles.AppendFrom(_pendingProjectileSpawn, i);
			_spawnedEvents.Add(new ProjectileSpawnedEvent(
				_projectiles.Entity[activeIndex],
				activeIndex));
		}

		_pendingProjectileSpawn.RemoveRange(0, spawnCount);
	}

	private void FlushProjectileDespawns()
	{
		if (!_hasPendingProjectileDespawns)
		{
			return;
		}

		int writeIndex = 0;
		int readIndex = 0;
		while (readIndex < _projectiles.Count)
		{
			if (_projectiles.PendingDespawn[readIndex])
			{
				ProjectileSnapshot snapshot = _projectiles.Snapshot(readIndex);
				_despawnedEvents.Add(new ProjectileDespawnedEvent(snapshot.Entity, in snapshot));
				_contactGates.Remove(snapshot.Entity);
				readIndex++;
				continue;
			}

			int survivorStartIndex = readIndex;
			while (readIndex < _projectiles.Count && !_projectiles.PendingDespawn[readIndex])
			{
				readIndex++;
			}

			int survivorCount = readIndex - survivorStartIndex;
			_projectiles.CopyRangeWithin(survivorStartIndex, writeIndex, survivorCount);
			writeIndex += survivorCount;
		}

		_projectiles.Count = writeIndex;
		_hasPendingProjectileDespawns = false;
	}

	private static void Drain<T>(List<T> source, List<T> destination)
	{
		if (source.Count == 0)
		{
			return;
		}

		destination.AddRange(source);
		source.Clear();
	}

	private void GateContact(EntityHandle projectile, EntityHandle target)
	{
		if (!_contactGates.TryGetValue(projectile, out HashSet<EntityHandle>? gatedTargets))
		{
			gatedTargets = new HashSet<EntityHandle>();
			_contactGates.Add(projectile, gatedTargets);
		}

		gatedTargets.Add(target);
	}

	private bool ShouldUseThreading(int count)
	{
		return ParallelSimulationEnabled && count >= MinimumParallelProjectileCount;
	}

	private IProjectileWorkScheduler SchedulerFor(bool useThreading)
	{
		return useThreading
			? _parallelScheduler
			: _serialScheduler;
	}

	private void SetStepTiming(long startTicks, bool usedThreading, int threadedChunkCount)
	{
		long elapsedMicroseconds =
			((Stopwatch.GetTimestamp() - startTicks) * 1000000L) / Stopwatch.Frequency;
		_lastStepMicroseconds = elapsedMicroseconds;
		_lastStepUsedThreading = usedThreading;
		_lastThreadedChunkCount = threadedChunkCount;
		_lastThreadedWorkerCount = TaskProjectileWorkScheduler.WorkerCountFor(threadedChunkCount);
		_lastThreadedStepMicroseconds = usedThreading ? elapsedMicroseconds : 0;
	}

	private int SanitizedChunkSize()
	{
		return ParallelChunkSize > 0 ? ParallelChunkSize : 1;
	}

	private static void ForEachProjectileRange(
		IProjectileWorkScheduler scheduler,
		int count,
		int chunkSize,
		int offset,
		ProjectileRangeAction action)
	{
		scheduler.ForEachRange(count, chunkSize, (rangeStart, rangeEnd) =>
			action(offset + rangeStart, offset + rangeEnd));
	}

	/// <summary>
	/// Range-local event buffer used while resolving projectile hits and child spawns.
	/// Each worker writes to its own instance, then ProjectileWorld merges these buffers
	/// by StartIndex so threaded simulation keeps the same event order as serial simulation.
	/// </summary>
	private sealed class ProjectileRangeEvents
	{
		// First projectile row covered by this range; used to restore deterministic merge order.
		public readonly int StartIndex;
		public readonly List<HitEvent> HitEvents = new(1);
		public readonly List<ChildProjectileSpawnRequestedEvent> ChildSpawnRequestedEvents = new(1);
		public readonly List<ContactGateEvent> ContactGates = new(1);
		// Set when this range marked at least one projectile for later compaction/despawn.
		public bool HasPendingProjectileDespawns;

		public ProjectileRangeEvents(int startIndex)
		{
			StartIndex = startIndex;
		}
	}

	private readonly struct ContactGateEvent
	{
		public readonly EntityHandle Projectile;
		public readonly EntityHandle Target;

		public ContactGateEvent(EntityHandle projectile, EntityHandle target)
		{
			Projectile = projectile;
			Target = target;
		}
	}
}
