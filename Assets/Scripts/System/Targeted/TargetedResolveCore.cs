using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targeted
{
    internal static class TargetedResolveCore
    {
        // Resolve-side cap bounds the running rank set and per-update chain walk. Authoring
        // validation reports values outside this limit before they reach this defensive clamp.
        internal const int MaxChainTargets = 32;

        internal static bool Resolve(
            Entity sourceEntity,
            in TargetedIdentityComponent identity,
            in CombatHitPayload payload,
            ref TargetedChainComponent chain,
            in TargetedResolveConfig config,
            in TargetedVfxIds vfxIds,
            in TargetedVfxSizeComponent vfxSize,
            in VfxTimingData timing,
            ref CombatKinematicsComponent kinematics,
            float deltaTime,
            NativeArray<Entity> targetEntities,
            NativeArray<TargetPosition> targetPositions,
            NativeArray<TargetCollisionShape> targetShapes,
            NativeArray<TargetFaction> targetFactions,
            NativeParallelMultiHashMap<long, int> occupiedTargetCells,
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            NativeQueue<CircularVfxSpawnRequest>.ParallelWriter circularVfxPending,
            NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter timedCircularVfxPending,
            NativeQueue<LineSegmentVfxSpawn>.ParallelWriter lineSegmentVfxPending,
            out int linksResolved)
        {
            linksResolved = 0;
            if (identity.Faction == CombatFaction.None)
            {
                return true;
            }

            int maxTargets = math.clamp(config.MaxTargets, 1, MaxChainTargets);
            chain.LinkGateRemaining -= deltaTime;

            int availableHits = config.ChainDelaySeconds <= 0f
                ? maxTargets
                : chain.LinkGateRemaining <= 0f
                    ? (int)math.floor(-chain.LinkGateRemaining / config.ChainDelaySeconds) + 1
                    : 0;
            availableHits = math.min(availableHits, maxTargets - chain.LinkIndex);
            int linksThisUpdate = 0;
            float2 currentPosition = chain.LinkTarget;

            for (int i = 0; i < availableHits; i++)
            {
                bool firstLink = chain.LinkIndex == 0;
                float2 searchFrom = firstLink ? chain.AcquireAnchor : currentPosition;
                float radius = firstLink ? config.AcquireRadius : config.ChainRadius;
                int rank = firstLink
                    ? math.clamp(identity.InstanceIndex, 0, MaxChainTargets - 1)
                    : 0;
                bool allowRestartFallback = firstLink && chain.LastTargetKey != 0;

                if (!TrySelectNthNearest(
                        searchFrom,
                        radius,
                        rank,
                        identity.Faction,
                        chain.LastTargetKey,
                        allowRestartFallback,
                        targetEntities,
                        targetPositions,
                        targetShapes,
                        targetFactions,
                        occupiedTargetCells,
                        out Entity targetEntity,
                        out float2 targetPosition))
                {
                    break;
                }

                chain.LinkSource = currentPosition;
                currentPosition = targetPosition;
                chain.LinkTarget = currentPosition;
                chain.LastTargetKey = TargetKey(targetEntity);

                hitWriter.Enqueue(new CombatHitEvent
                {
                    Source = sourceEntity,
                    Target = targetEntity,
                    DamageScale = math.pow(config.ChainDamageFalloff, chain.LinkIndex)
                });
                VfxEmit.EnqueueLineSegment(
                    vfxIds.LinkId,
                    chain.LinkSource,
                    chain.LinkTarget,
                    vfxSize.LinkWidth,
                    lineSegmentVfxPending);
                VfxEmit.Enqueue(
                    vfxIds.HitId,
                    chain.LinkTarget,
                    vfxSize.EffectSize,
                    timing,
                    circularVfxPending,
                    timedCircularVfxPending);

                chain.LinkIndex++;
                linksThisUpdate++;
                chain.LinkGateRemaining += config.ChainDelaySeconds;
            }

            if (linksThisUpdate > 0)
            {
                kinematics.Position = chain.LinkTarget;
                kinematics.Velocity = chain.LinkTarget - chain.LinkSource;
            }

            linksResolved = linksThisUpdate;
            return chain.LinkIndex >= maxTargets || (availableHits > 0 && linksThisUpdate == 0);
        }

        private static bool TrySelectNthNearest(
            float2 from,
            float radius,
            int rank,
            CombatFaction faction,
            int excludeKey,
            bool allowRestartFallback,
            NativeArray<Entity> targetEntities,
            NativeArray<TargetPosition> targetPositions,
            NativeArray<TargetCollisionShape> targetShapes,
            NativeArray<TargetFaction> targetFactions,
            NativeParallelMultiHashMap<long, int> occupiedTargetCells,
            out Entity selectedEntity,
            out float2 selectedPosition)
        {
            selectedEntity = Entity.Null;
            selectedPosition = default;
            int limit = math.clamp(rank + 1, 1, MaxChainTargets);
            FixedList512Bytes<Candidate> eligible = default;
            FixedList512Bytes<Candidate> excludedFallback = default;

            int2 min = CombatSpatialHash.MinCell(
                from - new float2(radius), CombatSpatialHash.AoeCellSize);
            int2 max = CombatSpatialHash.MaxCell(
                from + new float2(radius), CombatSpatialHash.AoeCellSize);
            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!occupiedTargetCells.TryGetFirstValue(
                            CombatSpatialHash.CellKey(x, y),
                            out int targetIndex,
                            out NativeParallelMultiHashMapIterator<long> iterator))
                    {
                        continue;
                    }

                    do
                    {
                        if (targetIndex < 0 || targetIndex >= targetEntities.Length
                            || targetFactions[targetIndex].Value == faction)
                        {
                            continue;
                        }

                        TargetCollisionShape target = targetShapes[targetIndex];
                        if (!CombatCollisionMath.BoundsIntersect(
                                from - new float2(radius),
                                from + new float2(radius),
                                target.BoundsMin,
                                target.BoundsMax)
                            || !CombatCollisionMath.Hit(
                                from,
                                radius,
                                default,
                                0f,
                                CombatShapeType.Circle,
                                targetPositions[targetIndex].Value,
                                target.Radius,
                                target.HalfExtents,
                                target.RotationRadians,
                                target.ShapeType))
                        {
                            continue;
                        }

                        Candidate candidate = new()
                        {
                            TargetIndex = targetIndex,
                            DistanceSquared = math.lengthsq(targetPositions[targetIndex].Value - from)
                        };
                        if (TargetKey(targetEntities[targetIndex]) == excludeKey)
                        {
                            if (allowRestartFallback)
                            {
                                InsertNearest(ref excludedFallback, candidate, limit);
                            }

                            continue;
                        }

                        InsertNearest(ref eligible, candidate, limit);
                    }
                    while (occupiedTargetCells.TryGetNextValue(out targetIndex, ref iterator));
                }
            }

            if (eligible.Length > 0)
            {
                Candidate selected = eligible[rank % eligible.Length];
                selectedEntity = targetEntities[selected.TargetIndex];
                selectedPosition = targetPositions[selected.TargetIndex].Value;
                return true;
            }

            if (excludedFallback.Length > 0)
            {
                Candidate selected = excludedFallback[rank % excludedFallback.Length];
                selectedEntity = targetEntities[selected.TargetIndex];
                selectedPosition = targetPositions[selected.TargetIndex].Value;
                return true;
            }

            return false;
        }

        private static void InsertNearest(
            ref FixedList512Bytes<Candidate> candidates,
            Candidate candidate,
            int limit)
        {
            // This bounded nearest set is also the target-index dedupe. A candidate displaced
            // from a full set cannot later re-enter: every retained candidate is closer.
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

        private static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        private struct Candidate
        {
            public int TargetIndex;
            public float DistanceSquared;
        }
    }
}
