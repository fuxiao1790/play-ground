using System.Collections.Generic;
using Godot;

/// <summary>
/// Pure-data runtime for scoped AOE simulation.
/// </summary>
public sealed class AoeWorld
{
	private readonly List<AoeSpawnCommand> _pendingSpawns = new(1);
	private readonly List<AoeSpawnCommand> _activeAoes = new(1);
	private readonly List<HitShape> _activeAoeShapes = new(1);
	private readonly List<Rect2> _activeAoeBounds = new(1);
	private readonly List<float> _activeLifetimes = new(1);
	private readonly List<bool> _pendingDespawn = new(1);
	private readonly TargetStore _targets = new();
	private readonly ShapeTable _aoeShapes = new();
	private readonly ShapeTable _targetShapes = new();
	private readonly SpatialHashTargetIndex _targetIndex = new();
	private readonly Dictionary<EntityHandle, Dictionary<EntityHandle, ContactState>> _contacts = new();
	private readonly List<int> _candidateTargets = new(16);
	private readonly List<EntityHandle> _staleContacts = new(4);
	private readonly List<AoeHitEvent> _hitEvents = new(1);
	private readonly List<AoeDespawnedEvent> _despawnedEvents = new(1);
	private HitShape[] _targetShapeCache = System.Array.Empty<HitShape>();
	private int _stepVersion;
	private float _elapsedSeconds;

	public int MaximumAoeCount { get; set; } = 10000;
	public int MaximumTargetCount { get; set; } = 100;
	public int ActiveCount => _activeAoes.Count + _pendingSpawns.Count;

	public void RegisterAoeType(int typeId, HitShape collision)
	{
		_aoeShapes.Set(typeId, collision);
	}

	public void RegisterTargetType(int typeId, HitShape collision)
	{
		_targetShapes.Set(typeId, collision);
	}

	public void SubmitSpawn(in AoeSpawnCommand command)
	{
		_pendingSpawns.Add(command);
	}

	public void SubmitTargets(IReadOnlyList<TargetSnapshot> snapshots)
	{
		_targets.Clear();
		int targetCount = System.Math.Min(snapshots.Count, MaximumTargetCount);
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

	public void Step(double delta)
	{
		FlushDespawns();
		PromotePendingSpawns();
		RefreshTargets();
		_stepVersion++;
		_elapsedSeconds += (float)delta;

		for (int i = 0; i < _activeAoes.Count; i++)
		{
			if (_pendingDespawn[i])
			{
				continue;
			}

			StepAoe(i, (float)delta);
		}
	}

	public void DrainEvents(
		List<AoeHitEvent> hitEvents,
		List<AoeDespawnedEvent> despawnedEvents)
	{
		if (_hitEvents.Count > 0)
		{
			hitEvents.AddRange(_hitEvents);
			_hitEvents.Clear();
		}

		if (_despawnedEvents.Count > 0)
		{
			despawnedEvents.AddRange(_despawnedEvents);
			_despawnedEvents.Clear();
		}
	}

	public void Clear()
	{
		_pendingSpawns.Clear();
		_activeAoes.Clear();
		_activeAoeShapes.Clear();
		_activeAoeBounds.Clear();
		_activeLifetimes.Clear();
		_pendingDespawn.Clear();
		_targets.Clear();
		_targetIndex.Clear();
		_contacts.Clear();
		_candidateTargets.Clear();
		_staleContacts.Clear();
		_hitEvents.Clear();
		_despawnedEvents.Clear();
		_elapsedSeconds = 0.0f;
	}

	private void PromotePendingSpawns()
	{
		if (_pendingSpawns.Count == 0)
		{
			return;
		}

		int availableSlots = MaximumAoeCount - _activeAoes.Count;
		if (availableSlots <= 0)
		{
			_pendingSpawns.Clear();
			return;
		}

		int spawnCount = System.Math.Min(_pendingSpawns.Count, availableSlots);
		for (int i = 0; i < spawnCount; i++)
		{
			AoeSpawnCommand command = _pendingSpawns[i];
			HitShape shape = _aoeShapes.Get(command.AoeTypeId, "AOE");
			_activeAoes.Add(command);
			_activeAoeShapes.Add(shape);
			_activeAoeBounds.Add(HitShapeMath.ComputeWorldBounds(command.Position, shape));
			_activeLifetimes.Add(command.LifetimeSeconds);
			_pendingDespawn.Add(false);
		}

		_pendingSpawns.RemoveRange(0, spawnCount);
	}

	private void StepAoe(int aoeIndex, float delta)
	{
		AoeSpawnCommand aoe = _activeAoes[aoeIndex];
		HitShape aoeShape = _activeAoeShapes[aoeIndex];
		Rect2 aoeBounds = _activeAoeBounds[aoeIndex];
		Dictionary<EntityHandle, ContactState> contacts = ContactsFor(aoe.AoeHandle);

		_candidateTargets.Clear();
		_targetIndex.Query(aoeBounds, _candidateTargets);
		for (int i = 0; i < _candidateTargets.Count; i++)
		{
			int targetIndex = _candidateTargets[i];
			if ((aoe.TargetMask & _targets.CollisionLayer[targetIndex]) == 0
				|| !aoeBounds.Intersects(_targets.WorldBounds[targetIndex]))
			{
				continue;
			}

			HitShape targetShape = _targetShapeCache[targetIndex];
			if (!HitShapeMath.Hit(
				aoe.Position,
				aoeShape,
				_targets.Position[targetIndex],
				targetShape))
			{
				continue;
			}

			ResolveTargetHit(aoe, targetIndex, contacts);
		}

		ReleaseExitedContacts(contacts);

		if (aoe.LifetimeSeconds <= 0.0f)
		{
			_pendingDespawn[aoeIndex] = true;
			return;
		}

		_activeLifetimes[aoeIndex] -= delta;
		if (_activeLifetimes[aoeIndex] <= 0.0f)
		{
			_pendingDespawn[aoeIndex] = true;
		}
	}

	private void ResolveTargetHit(
		in AoeSpawnCommand aoe,
		int targetIndex,
		Dictionary<EntityHandle, ContactState> contacts)
	{
		EntityHandle target = _targets.Entity[targetIndex];
		bool hasContact = contacts.TryGetValue(target, out ContactState contact);
		bool shouldHit = !hasContact || _elapsedSeconds >= contact.NextHitTimeSeconds;
		contact.LastSeenStep = _stepVersion;
		if (!shouldHit)
		{
			contacts[target] = contact;
			return;
		}

		contact.NextHitTimeSeconds = _elapsedSeconds + aoe.TickIntervalSeconds;
		if (hasContact)
		{
			contacts[target] = contact;
		}
		else
		{
			contacts.Add(target, contact);
		}

		_hitEvents.Add(new AoeHitEvent(
			aoe.AoeHandle,
			target,
			aoe.AoeTypeId,
			aoe.Position,
			aoe.Damage));
	}

	private Dictionary<EntityHandle, ContactState> ContactsFor(EntityHandle aoe)
	{
		if (!_contacts.TryGetValue(aoe, out Dictionary<EntityHandle, ContactState>? contacts))
		{
			contacts = new Dictionary<EntityHandle, ContactState>();
			_contacts.Add(aoe, contacts);
		}

		return contacts;
	}

	private void ReleaseExitedContacts(Dictionary<EntityHandle, ContactState> contacts)
	{
		if (contacts.Count == 0)
		{
			return;
		}

		_staleContacts.Clear();
		foreach ((EntityHandle target, ContactState contact) in contacts)
		{
			if (contact.LastSeenStep == _stepVersion)
			{
				continue;
			}

			_staleContacts.Add(target);
		}

		for (int i = 0; i < _staleContacts.Count; i++)
		{
			contacts.Remove(_staleContacts[i]);
		}
	}

	private void RefreshTargets()
	{
		if (_targetShapeCache.Length < _targets.Count)
		{
			System.Array.Resize(ref _targetShapeCache, _targets.Count);
		}

		for (int i = 0; i < _targets.Count; i++)
		{
			HitShape targetShape = _targetShapes.Get(_targets.TypeId[i], "AOE target");
			_targetShapeCache[i] = targetShape;
			_targets.WorldBounds[i] = HitShapeMath.ComputeWorldBounds(_targets.Position[i], targetShape);
		}

		_targetIndex.Build(_targets);
	}

	private void FlushDespawns()
	{
		int writeIndex = 0;
		for (int readIndex = 0; readIndex < _activeAoes.Count; readIndex++)
		{
			AoeSpawnCommand aoe = _activeAoes[readIndex];
			if (_pendingDespawn[readIndex])
			{
				_contacts.Remove(aoe.AoeHandle);
				_despawnedEvents.Add(new AoeDespawnedEvent(aoe.AoeHandle));
				continue;
			}

			if (writeIndex != readIndex)
			{
				_activeAoes[writeIndex] = _activeAoes[readIndex];
				_activeAoeShapes[writeIndex] = _activeAoeShapes[readIndex];
				_activeAoeBounds[writeIndex] = _activeAoeBounds[readIndex];
				_activeLifetimes[writeIndex] = _activeLifetimes[readIndex];
				_pendingDespawn[writeIndex] = false;
			}

			writeIndex++;
		}

		if (writeIndex == _activeAoes.Count)
		{
			return;
		}

		int removeCount = _activeAoes.Count - writeIndex;
		_activeAoes.RemoveRange(writeIndex, removeCount);
		_activeAoeShapes.RemoveRange(writeIndex, removeCount);
		_activeAoeBounds.RemoveRange(writeIndex, removeCount);
		_activeLifetimes.RemoveRange(writeIndex, removeCount);
		_pendingDespawn.RemoveRange(writeIndex, removeCount);
	}

	private struct ContactState
	{
		public float NextHitTimeSeconds;
		public int LastSeenStep;
	}

	private sealed class ShapeTable
	{
		private readonly List<HitShape> _shapes = new();
		private readonly List<bool> _assigned = new();

		public void Set(int typeId, HitShape shape)
		{
			while (_shapes.Count <= typeId)
			{
				_shapes.Add(default);
				_assigned.Add(false);
			}

			_shapes[typeId] = shape;
			_assigned[typeId] = true;
		}

		public HitShape Get(int typeId, string ownerName)
		{
			if (typeId < 0 || typeId >= _assigned.Count || !_assigned[typeId])
			{
				throw new System.InvalidOperationException($"Missing {ownerName} collision definition for type id {typeId}.");
			}

			return _shapes[typeId];
		}
	}

	private sealed class SpatialHashTargetIndex
	{
		private const float CellSize = 64.0f;

		private readonly Dictionary<long, List<int>> _cells = new();
		private int[] _targetStamps = System.Array.Empty<int>();
		private int _queryStamp;

		public void Build(TargetStore targets)
		{
			_cells.Clear();
			if (_targetStamps.Length < targets.Count)
			{
				System.Array.Resize(ref _targetStamps, targets.Count);
			}

			for (int i = 0; i < targets.Count; i++)
			{
				AddTarget(i, targets.WorldBounds[i]);
			}
		}

		public void Query(Rect2 bounds, List<int> results)
		{
			results.Clear();
			_queryStamp++;
			if (_queryStamp == int.MaxValue)
			{
				System.Array.Clear(_targetStamps);
				_queryStamp = 1;
			}

			CellCoordinate min = MinCell(bounds);
			CellCoordinate max = MaxCell(bounds);
			for (int y = min.Y; y <= max.Y; y++)
			{
				for (int x = min.X; x <= max.X; x++)
				{
					if (!_cells.TryGetValue(CellKey(x, y), out List<int>? targets))
					{
						continue;
					}

					for (int i = 0; i < targets.Count; i++)
					{
						int targetIndex = targets[i];
						if (_targetStamps[targetIndex] == _queryStamp)
						{
							continue;
						}

						_targetStamps[targetIndex] = _queryStamp;
						results.Add(targetIndex);
					}
				}
			}
		}

		public void Clear()
		{
			_cells.Clear();
			System.Array.Clear(_targetStamps);
			_queryStamp = 0;
		}

		private void AddTarget(int targetIndex, Rect2 bounds)
		{
			CellCoordinate min = MinCell(bounds);
			CellCoordinate max = MaxCell(bounds);
			for (int y = min.Y; y <= max.Y; y++)
			{
				for (int x = min.X; x <= max.X; x++)
				{
					long key = CellKey(x, y);
					if (!_cells.TryGetValue(key, out List<int>? targets))
					{
						targets = new List<int>(1);
						_cells.Add(key, targets);
					}

					targets.Add(targetIndex);
				}
			}
		}

		private static CellCoordinate MinCell(Rect2 bounds)
		{
			return new CellCoordinate(
				Mathf.FloorToInt(bounds.Position.X / CellSize),
				Mathf.FloorToInt(bounds.Position.Y / CellSize));
		}

		private static CellCoordinate MaxCell(Rect2 bounds)
		{
			Vector2 end = bounds.Position + bounds.Size;
			return new CellCoordinate(
				Mathf.FloorToInt(end.X / CellSize),
				Mathf.FloorToInt(end.Y / CellSize));
		}

		private static long CellKey(int x, int y)
		{
			return ((long)x << 32) ^ (uint)y;
		}

		private readonly struct CellCoordinate
		{
			public readonly int X;
			public readonly int Y;

			public CellCoordinate(int x, int y)
			{
				X = x;
				Y = y;
			}
		}
	}
}
