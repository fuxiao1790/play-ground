using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targeted
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetSpatialHashSystem))]
    [UpdateAfter(typeof(CombatArmingSystem))]
    [UpdateBefore(typeof(CombatApplyFinalizeSingleSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(TargetedSpawnExpansionSystem))]
    public partial struct TargetedResolveSystem : ISystem
    {
        // Resolve-side cap bounds the running rank set and per-update chain walk. Authoring
        // validation reports values outside this limit before they reach this defensive clamp.
        internal const int MaxChainCount = 32;

        private EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TargetedTag>()
                .WithAll<Active>()
                .WithAllRW<TargetedChainComponent>()
                .WithAllRW<CombatKinematicsComponent>()
                .WithAll<TargetedIdentityComponent>()
                .WithAll<TargetedResolveConfig>()
                .WithAll<CombatHitPayload>()
                .WithAll<TargetedVfxIds>()
                .WithAll<TargetedVfxSizeComponent>()
                .WithAll<VfxTimingData>()
                .WithDisabled<ArmingTag>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_query.IsEmpty)
            {
                return;
            }

            TargetSpatialHashSingleton hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);
            RefRW<CombatHitDispatchSingleton> hitDispatch =
                SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();
            bool collectTargetedLinks = SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(
                out RefRW<CombatStatsSingleton> stats);

            JobHandle handle = new TargetedResolveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetShapes = hash.TargetShapes.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                OccupiedTargetCells = hash.AoeOccupiedCells,
                HitWriter = hitDispatch.ValueRO.HitQueue.AsParallelWriter(),
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter(),
                LineSegmentVfxPending = vfx.ValueRO.PendingLineSegmentSpawns.AsParallelWriter(),
                CollectTargetedLinks = collectTargetedLinks ? 1 : 0,
                TargetedLinkCounts = collectTargetedLinks
                    ? stats.ValueRO.TargetedLinkCounts.AsParallelWriter()
                    : default
            }.ScheduleParallel(_query, state.Dependency);

            hitDispatch.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(hitDispatch.ValueRW.ProducerHandle, handle);
            vfx.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, handle);
            if (collectTargetedLinks)
            {
                stats.ValueRW.TargetedLinkProducerHandle =
                    JobHandle.CombineDependencies(stats.ValueRW.TargetedLinkProducerHandle, handle);
            }
            RefRW<TargetSpatialHashSingleton> hashRw =
                SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
            hashRw.ValueRW.ConsumerHandle =
                JobHandle.CombineDependencies(hashRw.ValueRW.ConsumerHandle, handle);
            state.Dependency = handle;
        }

        [BurstCompile]
        [WithAll(typeof(TargetedTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct TargetedResolveJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public NativeQueue<CircularVfxSpawnRequest>.ParallelWriter CircularVfxPending;
            public NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter TimedCircularVfxPending;
            public NativeQueue<LineSegmentVfxSpawn>.ParallelWriter LineSegmentVfxPending;
            public int CollectTargetedLinks;
            public NativeQueue<int>.ParallelWriter TargetedLinkCounts;

            private void Execute(
                Entity entity,
                in TargetedIdentityComponent identity,
                in CombatHitPayload payload,
                ref TargetedChainComponent chain,
                in TargetedResolveConfig config,
                in TargetedVfxIds vfxIds,
                in TargetedVfxSizeComponent vfxSize,
                in VfxTimingData timing,
                ref CombatKinematicsComponent kinematics,
                EnabledRefRW<Active> active,
                EnabledRefRW<ArmingTag> arming)
            {
                bool walkFinished = Walk(
                    entity, identity, ref chain, config, vfxIds, vfxSize, timing,
                    ref kinematics, out int linksResolved);
                if (CollectTargetedLinks != 0 && linksResolved > 0)
                {
                    TargetedLinkCounts.Enqueue(linksResolved);
                }

                if (!walkFinished)
                {
                    return;
                }

                // The chain's lifetime *is* its walk: the instant the last link lands, or a link
                // finds nothing, the instance is done. The compiled CombatLifetimeComponent is a
                // fail-safe behind this, not the normal despawn path.
                CombatDeathUtility.Kill(active, arming);
                VfxEmit.Enqueue(
                    vfxIds.ExpireId,
                    kinematics.Position,
                    vfxSize.EffectSize,
                    timing,
                    CircularVfxPending,
                    TimedCircularVfxPending);
            }

            private bool Walk(
                Entity sourceEntity,
                in TargetedIdentityComponent identity,
                ref TargetedChainComponent chain,
                in TargetedResolveConfig config,
                in TargetedVfxIds vfxIds,
                in TargetedVfxSizeComponent vfxSize,
                in VfxTimingData timing,
                ref CombatKinematicsComponent kinematics,
                out int linksResolved)
            {
                linksResolved = 0;
                if (identity.Faction == CombatFaction.None)
                {
                    return true;
                }

                int chainCount = math.clamp(config.ChainCount, 1, MaxChainCount);
                chain.LinkGateRemaining -= DeltaTime;

                int availableHits = config.ChainDelay <= 0f
                    ? chainCount
                    : chain.LinkGateRemaining <= 0f
                        ? (int)math.floor(-chain.LinkGateRemaining / config.ChainDelay) + 1
                        : 0;
                availableHits = math.min(availableHits, chainCount - chain.LinkIndex);
                int linksThisUpdate = 0;
                float2 currentPosition = chain.LinkTarget;

                for (int i = 0; i < availableHits; i++)
                {
                    bool firstLink = chain.LinkIndex == 0;
                    // Link 0 searches around the acquisition anchor; every later link searches
                    // from the chain head. One authored distance governs both.
                    float2 searchFrom = firstLink ? chain.AcquireAnchor : currentPosition;
                    int rank = firstLink
                        ? math.clamp(identity.InstanceIndex, 0, MaxChainCount - 1)
                        : 0;

                    if (!TrySelectNthNearest(
                            searchFrom,
                            config.ChainDistance,
                            rank,
                            identity.Faction,
                            chain.LastTargetKey,
                            out Entity targetEntity,
                            out float2 targetPosition))
                    {
                        break;
                    }

                    chain.LinkSource = currentPosition;
                    currentPosition = targetPosition;
                    chain.LinkTarget = currentPosition;
                    chain.LastTargetKey = TargetKey(targetEntity);

                    HitWriter.Enqueue(new CombatHitEvent
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
                        LineSegmentVfxPending);
                    VfxEmit.Enqueue(
                        vfxIds.HitId,
                        chain.LinkTarget,
                        vfxSize.EffectSize,
                        timing,
                        CircularVfxPending,
                        TimedCircularVfxPending);

                    chain.LinkIndex++;
                    linksThisUpdate++;
                    chain.LinkGateRemaining += config.ChainDelay;
                }

                if (linksThisUpdate > 0)
                {
                    kinematics.Position = chain.LinkTarget;
                    kinematics.Velocity = chain.LinkTarget - chain.LinkSource;
                }

                linksResolved = linksThisUpdate;

                // An update that landed a link never ends the instance. The render mirror is only
                // written above, and render prep runs after this system, so dying here would draw
                // the sprite nowhere for a chainDelay = 0 walk. Holding the slot for one more
                // update renders the sprite once on the final target, then expires.
                if (linksThisUpdate > 0)
                {
                    return false;
                }

                return chain.LinkIndex >= chainCount || availableHits > 0;
            }

            private bool TrySelectNthNearest(
                float2 from,
                float radius,
                int rank,
                CombatFaction faction,
                int excludeKey,
                out Entity selectedEntity,
                out float2 selectedPosition)
            {
                selectedEntity = Entity.Null;
                selectedPosition = default;
                int limit = math.clamp(rank + 1, 1, MaxChainCount);
                FixedList512Bytes<Candidate> eligible = default;

                int2 min = CombatSpatialHash.MinCell(
                    from - new float2(radius), CombatSpatialHash.AoeCellSize);
                int2 max = CombatSpatialHash.MaxCell(
                    from + new float2(radius), CombatSpatialHash.AoeCellSize);
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!OccupiedTargetCells.TryGetFirstValue(
                                CombatSpatialHash.CellKey(x, y),
                                out int targetIndex,
                                out NativeParallelMultiHashMapIterator<long> iterator))
                        {
                            continue;
                        }

                        do
                        {
                            if (targetIndex < 0 || targetIndex >= TargetEntities.Length
                                || TargetFactions[targetIndex].Value == faction
                                || TargetKey(TargetEntities[targetIndex]) == excludeKey)
                            {
                                continue;
                            }

                            TargetCollisionShape target = TargetShapes[targetIndex];
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
                                    TargetPositions[targetIndex].Value,
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
                                DistanceSquared = math.lengthsq(TargetPositions[targetIndex].Value - from)
                            }, limit);
                        }
                        while (OccupiedTargetCells.TryGetNextValue(out targetIndex, ref iterator));
                    }
                }

                if (eligible.Length == 0)
                {
                    return false;
                }

                Candidate selected = eligible[rank % eligible.Length];
                selectedEntity = TargetEntities[selected.TargetIndex];
                selectedPosition = TargetPositions[selected.TargetIndex].Value;
                return true;
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
}
