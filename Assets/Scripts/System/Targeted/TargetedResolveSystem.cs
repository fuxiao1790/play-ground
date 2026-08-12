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
                TargetSnapshot = new TargetedAcquisition.Snapshot(
                    hash.TargetEntities.AsArray(),
                    hash.TargetPositions.AsArray(),
                    hash.TargetShapes.AsArray(),
                    hash.TargetFactions.AsArray(),
                    hash.AoeOccupiedCells),
                HitWriter = hitDispatch.ValueRO.HitQueue.AsParallelWriter(),
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter(),
                LineSegmentVfxPending = vfx.ValueRO.PendingLineSegmentSpawns.AsParallelWriter(),
                CollectTargetedLinks = collectTargetedLinks ? 1 : 0,
                TargetedLinkCounts = collectTargetedLinks
                    ? stats.ValueRO.TargetedLinkCounts.AsParallelWriter()
                    : default,
                SpawnTemplateDeltas =
                    SystemAPI.GetSingleton<SpawnTemplateRegistryState>().Deltas.AsParallelWriter()
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
            public TargetedAcquisition.Snapshot TargetSnapshot;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public NativeQueue<ImpactCircleVfxEvent>.ParallelWriter CircularVfxPending;
            public NativeQueue<LingeringCircleVfxEvent>.ParallelWriter TimedCircularVfxPending;
            public NativeQueue<LineSegmentVfxEvent>.ParallelWriter LineSegmentVfxPending;
            public int CollectTargetedLinks;
            public NativeQueue<int>.ParallelWriter TargetedLinkCounts;
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter SpawnTemplateDeltas;

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
                SpawnTemplateRefEmit.ReleaseTargeted(in payload, SpawnTemplateDeltas);
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

                    if (!TargetedAcquisition.TrySelectNthNearest(
                            TargetSnapshot,
                            searchFrom,
                            config.ChainDistance,
                            rank,
                            identity.Faction,
                            chain.LastTargetKey,
                            MaxChainCount,
                            out Entity targetEntity,
                            out float2 targetPosition))
                    {
                        break;
                    }

                    chain.LinkSource = firstLink ? chain.Origin : currentPosition;
                    currentPosition = targetPosition;
                    chain.LinkTarget = currentPosition;
                    chain.LastTargetKey = TargetedAcquisition.TargetKey(targetEntity);

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

        }
    }
}
