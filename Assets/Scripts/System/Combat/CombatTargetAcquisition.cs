using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Targets;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat
{
    [BurstCompile]
    public static class CombatTargetAcquisition
    {
        public readonly struct Snapshot
        {
            [ReadOnly] public readonly NativeArray<Entity> TargetEntities;
            [ReadOnly] public readonly NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public readonly NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public readonly NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public readonly NativeParallelMultiHashMap<long, int> OccupiedCells;

            public Snapshot(
                NativeArray<Entity> targetEntities,
                NativeArray<TargetPosition> targetPositions,
                NativeArray<TargetCollisionShape> targetShapes,
                NativeArray<TargetFaction> targetFactions,
                NativeParallelMultiHashMap<long, int> occupiedCells)
            {
                TargetEntities = targetEntities;
                TargetPositions = targetPositions;
                TargetShapes = targetShapes;
                TargetFactions = targetFactions;
                OccupiedCells = occupiedCells;
            }
        }

        public static bool TryNearestEligible(
            in Snapshot snapshot,
            float2 from,
            float radius,
            CombatFaction faction,
            out Entity entity,
            out float2 position) =>
            TrySelectNthNearest(snapshot, from, radius, 0, faction, 0, out entity, out position);

        public static bool TrySelectNthNearest(
            in Snapshot snapshot,
            float2 from,
            float radius,
            int rank,
            CombatFaction faction,
            int excludeKey,
            out Entity entity,
            out float2 position)
        {
            return TrySelectNthNearest(
                snapshot,
                from,
                radius,
                rank,
                faction,
                excludeKey,
                int.MaxValue,
                out entity,
                out position);
        }

        public static bool TrySelectNthNearest(
            in Snapshot snapshot,
            float2 from,
            float radius,
            int rank,
            CombatFaction faction,
            int excludeKey,
            int maxChainCount,
            out Entity entity,
            out float2 position)
        {
            entity = Entity.Null;
            position = default;
            int limit = math.clamp(rank + 1, 1, maxChainCount);
            FixedList512Bytes<Candidate> eligible = default;

            float2 boundsMin = from - new float2(radius);
            float2 boundsMax = from + new float2(radius);
            int2 min = CombatSpatialHash.MinCell(boundsMin, CombatSpatialHash.AoeCellSize);
            int2 max = CombatSpatialHash.MaxCell(boundsMax, CombatSpatialHash.AoeCellSize);
            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!snapshot.OccupiedCells.TryGetFirstValue(
                            CombatSpatialHash.CellKey(x, y),
                            out int targetIndex,
                            out NativeParallelMultiHashMapIterator<long> iterator))
                    {
                        continue;
                    }

                    do
                    {
                        if (targetIndex < 0
                            || targetIndex >= snapshot.TargetEntities.Length)
                        {
                            continue;
                        }

                        TargetFaction candidateFaction = snapshot.TargetFactions[targetIndex];
                        if (!TargetFaction.CanHit(faction, in candidateFaction)
                            || TargetKey(snapshot.TargetEntities[targetIndex]) == excludeKey)
                        {
                            continue;
                        }

                        TargetCollisionShape target = snapshot.TargetShapes[targetIndex];
                        if (!CombatCollisionMath.BoundsIntersect(
                                boundsMin,
                                boundsMax,
                                target.BoundsMin,
                                target.BoundsMax)
                            || !CombatCollisionMath.Hit(
                                from,
                                radius,
                                default,
                                0f,
                                CombatShapeType.Circle,
                                snapshot.TargetPositions[targetIndex].Value,
                                target.Radius,
                                target.HalfExtents,
                                target.RotationRadians,
                                target.ShapeType))
                        {
                            continue;
                        }

                        InsertNearest(ref eligible, new Candidate
                        {
                            TargetIndex = targetIndex,
                            DistanceSquared = math.lengthsq(
                                snapshot.TargetPositions[targetIndex].Value - from)
                        }, limit);
                    }
                    while (snapshot.OccupiedCells.TryGetNextValue(out targetIndex, ref iterator));
                }
            }

            if (eligible.Length == 0)
            {
                return false;
            }

            Candidate selected = eligible[rank % eligible.Length];
            entity = snapshot.TargetEntities[selected.TargetIndex];
            position = snapshot.TargetPositions[selected.TargetIndex].Value;
            return true;
        }

        public static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        private static void InsertNearest(
            ref FixedList512Bytes<Candidate> candidates,
            Candidate candidate,
            int limit)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].TargetIndex == candidate.TargetIndex)
                {
                    return;
                }
            }

            int insertAt = candidates.Length;
            for (int i = 0; i < candidates.Length; i++)
            {
                Candidate other = candidates[i];
                if (candidate.DistanceSquared < other.DistanceSquared
                    || (candidate.DistanceSquared == other.DistanceSquared
                        && candidate.TargetIndex < other.TargetIndex))
                {
                    insertAt = i;
                    break;
                }
            }

            if (insertAt >= limit)
            {
                return;
            }

            if (candidates.Length < limit)
            {
                candidates.Add(default);
            }

            for (int i = candidates.Length - 1; i > insertAt; i--)
            {
                candidates[i] = candidates[i - 1];
            }

            candidates[insertAt] = candidate;
        }

        private struct Candidate
        {
            public int TargetIndex;
            public float DistanceSquared;
        }
    }
}
