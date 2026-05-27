using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// ProjectileWorld collision system that owns baked shape tables and broad-phase filters.
/// </summary>
public class Collision
{
	private readonly BroadPhaseProjectileFilterChain[] collisionFilters;
	private readonly ShapeCollision shapeCollisionDetector;
	private readonly HitShapeTable projectileShapes = new();
	private readonly HitShapeTable targetShapes = new();

	public Collision(params BroadPhaseProjectileFilterChain[] collisionFilters)
	{
		this.collisionFilters = collisionFilters;
		this.shapeCollisionDetector = new ShapeCollision(targetShapes);
	}

	public void RegisterProjectileDefinition(ProjectileDefinition definition)
	{
		projectileShapes.Set(definition.TypeId, definition.Collision);
	}

	public void RegisterTargetDefinition(TargetDefinition definition)
	{
		targetShapes.Set(definition.TypeId, definition.Collision);
	}

	public void BuildTargets(TargetStore targets)
	{
		RefreshTargetBounds(targets);

		foreach (BroadPhaseProjectileFilterChain filter in collisionFilters)
		{
			filter.Build(targets);
		}

		shapeCollisionDetector.Build(targets);
	}

	// Reads: projectile position/type. Writes: projectile world bounds by range.
	public void UpdateProjectileBounds(
		ProjectileStore projectiles,
		int startIndex,
		int endIndex)
	{
		for (int i = startIndex; i < endIndex; i++)
		{
			if (projectiles.PendingDespawn[i])
			{
				continue;
			}

			HitShape projectileShape = projectileShapes.Get(projectiles.TypeId[i], "projectile");
			projectiles.WorldBounds[i] =
				HitShapeMath.ComputeWorldBounds(projectiles.Position[i], projectileShape);
		}
	}

	// Reads/writes: contact gates. Runs serial on the world thread.
	public void ReleaseExitedContactGates(
		ProjectileStore projectiles,
		TargetStore targets,
		Dictionary<EntityHandle, HashSet<EntityHandle>> contactGates,
		List<EntityHandle> releasedGates,
		int startIndex,
		int endIndex)
	{
		if (contactGates.Count == 0)
		{
			return;
		}

		for (int i = startIndex; i < endIndex; i++)
		{
			ReleaseExitedContactGatesOne(projectiles, targets, contactGates, releasedGates, i);
		}
	}

	// Reads: projectile position/type/mask/bounds, target arrays, gates
	// Writes: Hit, HitTargetIndex
	public void Apply(
		ProjectileStore projectiles,
		TargetStore targets,
		Dictionary<EntityHandle, HashSet<EntityHandle>> contactGates,
		int startIndex,
		int endIndex)
	{
		bool hasContactGates = contactGates.Count > 0;
		for (int i = startIndex; i < endIndex; i++)
		{
			ApplyOne(projectiles, targets, contactGates, hasContactGates, i);
		}
	}

	private void ReleaseExitedContactGatesOne(
		ProjectileStore projectiles,
		TargetStore targets,
		Dictionary<EntityHandle, HashSet<EntityHandle>> contactGates,
		List<EntityHandle> releasedGates,
		int projectileIndex)
	{
		if (projectiles.PendingDespawn[projectileIndex])
		{
			return;
		}

		contactGates.TryGetValue(
			projectiles.Entity[projectileIndex],
			out HashSet<EntityHandle>? gatedTargets);
		if (gatedTargets == null)
		{
			return;
		}

		HitShape projectileShape = projectileShapes.Get(projectiles.TypeId[projectileIndex], "projectile");
		ReleaseExitedGates(
			projectiles,
			projectileIndex,
			projectileShape,
			targets,
			gatedTargets,
			releasedGates);
		if (gatedTargets != null && gatedTargets.Count == 0)
		{
			contactGates.Remove(projectiles.Entity[projectileIndex]);
		}
	}

	private void ApplyOne(
		ProjectileStore projectiles,
		TargetStore targets,
		Dictionary<EntityHandle, HashSet<EntityHandle>> contactGates,
		bool hasContactGates,
		int projectileIndex)
	{
		projectiles.Hit[projectileIndex] = false;
		projectiles.HitTargetIndex[projectileIndex] = -1;
		if (projectiles.PendingDespawn[projectileIndex])
		{
			return;
		}

		HashSet<EntityHandle>? gatedTargets = null;
		if (hasContactGates)
		{
			contactGates.TryGetValue(
				projectiles.Entity[projectileIndex],
				out gatedTargets);
		}

		foreach (BroadPhaseProjectileFilterChain filter in collisionFilters)
		{
			if (!filter.Filter(projectiles, projectileIndex))
			{
				return;
			}
		}

		HitShape projectileShape = projectileShapes.Get(projectiles.TypeId[projectileIndex], "projectile");
		shapeCollisionDetector.Hit(projectiles, projectileIndex, projectileShape, targets, gatedTargets);
	}

	private void ReleaseExitedGates(
		ProjectileStore projectiles,
		int projectileIndex,
		in HitShape projectileShape,
		TargetStore targets,
		HashSet<EntityHandle>? gatedTargets,
		List<EntityHandle> releasedGates)
	{
		if (gatedTargets == null || gatedTargets.Count == 0)
		{
			return;
		}

		releasedGates.Clear();
		foreach (EntityHandle targetHandle in gatedTargets)
		{
			if (!IsStillOverlapping(projectiles, projectileIndex, projectileShape, targets, targetHandle))
			{
				releasedGates.Add(targetHandle);
			}
		}

		for (int i = 0; i < releasedGates.Count; i++)
		{
			gatedTargets.Remove(releasedGates[i]);
		}
	}

	private bool IsStillOverlapping(
		ProjectileStore projectiles,
		int projectileIndex,
		in HitShape projectileShape,
		TargetStore targets,
		EntityHandle targetHandle)
	{
		for (int i = 0; i < targets.Count; i++)
		{
			if (targets.Entity[i] != targetHandle
				|| (projectiles.TargetMask[projectileIndex] & targets.CollisionLayer[i]) == 0)
			{
				continue;
			}

			if (!projectiles.WorldBounds[projectileIndex].Intersects(targets.WorldBounds[i]))
			{
				return false;
			}

			HitShape targetShape = targetShapes.Get(targets.TypeId[i], "target");
			return HitShapeMath.Hit(
				projectiles.Position[projectileIndex],
				projectileShape,
				targets.Position[i],
				targetShape);
		}

		return false;
	}

	private void RefreshTargetBounds(TargetStore targets)
	{
		for (int i = 0; i < targets.Count; i++)
		{
			HitShape targetShape = targetShapes.Get(targets.TypeId[i], "target");
			targets.WorldBounds[i] = HitShapeMath.ComputeWorldBounds(targets.Position[i], targetShape);
		}
	}

	/// <summary>
	/// Broad-phase filter built from target snapshots before projectile narrow-phase checks.
	/// </summary>
	public interface BroadPhaseProjectileFilterChain
	{
		void Build(TargetStore targets);

		bool Filter(ProjectileStore projectiles, int projectileIndex);
	}

	/// <summary>
	/// Dense type-id lookup table for baked projectile or target hit shapes.
	/// </summary>
	private sealed class HitShapeTable
	{
		private readonly List<HitShape> shapes = new();
		private readonly List<bool> assigned = new();

		public void Set(int typeId, HitShape shape)
		{
			if (typeId < 0)
			{
				throw new InvalidOperationException("Collision type id must be zero or greater.");
			}

			while (shapes.Count <= typeId)
			{
				shapes.Add(default);
				assigned.Add(false);
			}

			shapes[typeId] = shape;
			assigned[typeId] = true;
		}

		public HitShape Get(int typeId, string ownerName)
		{
			if (typeId < 0 || typeId >= assigned.Count || !assigned[typeId])
			{
				throw new InvalidOperationException($"Missing {ownerName} collision definition for type id {typeId}.");
			}

			return shapes[typeId];
		}
	}

	/// <summary>
	/// Broad-phase filter that stores target bounds in a simple AABB tree.
	/// </summary>
	public class AABBTreeFilter : BroadPhaseProjectileFilterChain
	{
		private const int EmptyNodeIndex = -1;

		private readonly List<TreeNode> nodes = new();
		private readonly List<Rect2> targetBounds = new();
		private int rootNodeIndex = EmptyNodeIndex;

		public void Build(TargetStore targets)
		{
			nodes.Clear();
			targetBounds.Clear();
			rootNodeIndex = EmptyNodeIndex;

			for (int i = 0; i < targets.Count; i++)
			{
				targetBounds.Add(targets.WorldBounds[i]);
			}

			if (targetBounds.Count == 0)
			{
				return;
			}

			rootNodeIndex = BuildNode(0, targetBounds.Count);
		}

		public bool Filter(ProjectileStore projectiles, int projectileIndex)
		{
			if (rootNodeIndex == EmptyNodeIndex)
			{
				return false;
			}

			return IntersectsNode(rootNodeIndex, projectiles.WorldBounds[projectileIndex]);
		}

		private int BuildNode(int start, int count)
		{
			Rect2 bounds = targetBounds[start];
			for (int i = start + 1; i < start + count; i++)
			{
				bounds = Merge(bounds, targetBounds[i]);
			}

			int nodeIndex = nodes.Count;
			nodes.Add(new TreeNode(bounds, EmptyNodeIndex, EmptyNodeIndex));

			if (count == 1)
			{
				return nodeIndex;
			}

			int splitAxis = bounds.Size.X >= bounds.Size.Y ? 0 : 1;
			targetBounds.Sort(start, count, new RectCenterComparer(splitAxis));

			int leftCount = count / 2;
			int rightCount = count - leftCount;
			int leftNodeIndex = BuildNode(start, leftCount);
			int rightNodeIndex = BuildNode(start + leftCount, rightCount);
			nodes[nodeIndex] = new TreeNode(bounds, leftNodeIndex, rightNodeIndex);
			return nodeIndex;
		}

		private bool IntersectsNode(int nodeIndex, Rect2 projectileBounds)
		{
			TreeNode node = nodes[nodeIndex];
			if (!projectileBounds.Intersects(node.Bounds))
			{
				return false;
			}

			if (node.LeftNodeIndex == EmptyNodeIndex)
			{
				return true;
			}

			return IntersectsNode(node.LeftNodeIndex, projectileBounds)
				|| IntersectsNode(node.RightNodeIndex, projectileBounds);
		}

		private static Rect2 Merge(Rect2 left, Rect2 right)
		{
			Vector2 min = new(
				Mathf.Min(left.Position.X, right.Position.X),
				Mathf.Min(left.Position.Y, right.Position.Y));
			Vector2 leftEnd = left.Position + left.Size;
			Vector2 rightEnd = right.Position + right.Size;
			Vector2 max = new(
				Mathf.Max(leftEnd.X, rightEnd.X),
				Mathf.Max(leftEnd.Y, rightEnd.Y));

			return new Rect2(min, max - min);
		}

		private readonly struct TreeNode
		{
			public readonly Rect2 Bounds;
			public readonly int LeftNodeIndex;
			public readonly int RightNodeIndex;

			public TreeNode(Rect2 bounds, int leftNodeIndex, int rightNodeIndex)
			{
				Bounds = bounds;
				LeftNodeIndex = leftNodeIndex;
				RightNodeIndex = rightNodeIndex;
			}
		}

		private sealed class RectCenterComparer : IComparer<Rect2>
		{
			private readonly int axis;

			public RectCenterComparer(int axis)
			{
				this.axis = axis;
			}

			public int Compare(Rect2 left, Rect2 right)
			{
				float leftCenter = axis == 0
					? left.Position.X + (left.Size.X * 0.5f)
					: left.Position.Y + (left.Size.Y * 0.5f);
				float rightCenter = axis == 0
					? right.Position.X + (right.Size.X * 0.5f)
					: right.Position.Y + (right.Size.Y * 0.5f);

				return leftCenter.CompareTo(rightCenter);
			}
		}
	}

	/// <summary>
	/// Broad-phase filter that marks occupied target cells in a fixed-size spatial hash.
	/// </summary>
	public class SpatialHashFilter : BroadPhaseProjectileFilterChain
	{
		private const float DefaultCellSize = 64.0f;
		private readonly HashSet<long> occupiedCells = new();
		private readonly float cellSize;

		public SpatialHashFilter(float cellSize = DefaultCellSize)
		{
			this.cellSize = cellSize > 0.0f ? cellSize : DefaultCellSize;
		}

		public void Build(TargetStore targets)
		{
			occupiedCells.Clear();

			for (int i = 0; i < targets.Count; i++)
			{
				AddBounds(targets.WorldBounds[i]);
			}
		}

		public bool Filter(ProjectileStore projectiles, int projectileIndex)
		{
			if (occupiedCells.Count == 0)
			{
				return false;
			}

			CellCoordinate min = MinCell(projectiles.WorldBounds[projectileIndex]);
			CellCoordinate max = MaxCell(projectiles.WorldBounds[projectileIndex]);

			for (int y = min.Y; y <= max.Y; y++)
			{
				for (int x = min.X; x <= max.X; x++)
				{
					if (occupiedCells.Contains(CellKey(x, y)))
					{
						return true;
					}
				}
			}

			return false;
		}

		private void AddBounds(Rect2 bounds)
		{
			CellCoordinate min = MinCell(bounds);
			CellCoordinate max = MaxCell(bounds);

			for (int y = min.Y; y <= max.Y; y++)
			{
				for (int x = min.X; x <= max.X; x++)
				{
					occupiedCells.Add(CellKey(x, y));
				}
			}
		}

		private CellCoordinate MinCell(Rect2 bounds)
		{
			return new CellCoordinate(
				Mathf.FloorToInt(bounds.Position.X / cellSize),
				Mathf.FloorToInt(bounds.Position.Y / cellSize));
		}

		private CellCoordinate MaxCell(Rect2 bounds)
		{
			Vector2 end = bounds.Position + bounds.Size;
			return new CellCoordinate(
				Mathf.FloorToInt(end.X / cellSize),
				Mathf.FloorToInt(end.Y / cellSize));
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

	/// <summary>
	/// Cached target-shape narrow-phase runner for the current target snapshot.
	/// </summary>
	private sealed class ShapeCollision
	{
		private readonly HitShapeTable targetShapeTable;
		private readonly List<Rect2> targetBounds = new();
		private readonly List<HitShape> targetShapes = new();

		public ShapeCollision(HitShapeTable targetShapeTable)
		{
			this.targetShapeTable = targetShapeTable;
		}

		public void Build(TargetStore targets)
		{
			targetBounds.Clear();
			targetShapes.Clear();

			for (int i = 0; i < targets.Count; i++)
			{
				targetBounds.Add(targets.WorldBounds[i]);
				targetShapes.Add(targetShapeTable.Get(targets.TypeId[i], "target"));
			}
		}

		public void Hit(
			ProjectileStore projectiles,
			int projectileIndex,
			in HitShape projectileShape,
			TargetStore targets,
			HashSet<EntityHandle>? gatedTargets)
		{
			projectiles.Hit[projectileIndex] = false;
			projectiles.HitTargetIndex[projectileIndex] = -1;

			if (targetShapes.Count == targets.Count)
			{
				for (int i = 0; i < targetShapes.Count; i++)
				{
					if ((projectiles.TargetMask[projectileIndex] & targets.CollisionLayer[i]) == 0
						|| IsGated(gatedTargets, targets.Entity[i]))
					{
						continue;
					}

					if (!projectiles.WorldBounds[projectileIndex].Intersects(targetBounds[i]))
					{
						continue;
					}

					HitShape targetShape = targetShapes[i];
					if (HitShapeMath.Hit(
						projectiles.Position[projectileIndex],
						projectileShape,
						targets.Position[i],
						targetShape))
					{
						projectiles.Hit[projectileIndex] = true;
						projectiles.HitTargetIndex[projectileIndex] = i;
						return;
					}
				}

				return;
			}

			for (int i = 0; i < targets.Count; i++)
			{
				if ((projectiles.TargetMask[projectileIndex] & targets.CollisionLayer[i]) == 0
					|| IsGated(gatedTargets, targets.Entity[i]))
				{
					continue;
				}

				if (!projectiles.WorldBounds[projectileIndex].Intersects(targets.WorldBounds[i]))
				{
					continue;
				}

				HitShape targetShape = targetShapeTable.Get(targets.TypeId[i], "target");
				if (HitShapeMath.Hit(
					projectiles.Position[projectileIndex],
					projectileShape,
					targets.Position[i],
					targetShape))
				{
					projectiles.Hit[projectileIndex] = true;
					projectiles.HitTargetIndex[projectileIndex] = i;
					return;
				}
			}
		}

		private static bool IsGated(HashSet<EntityHandle>? gatedTargets, EntityHandle target)
		{
			return gatedTargets != null && gatedTargets.Contains(target);
		}
	}
}
