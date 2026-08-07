using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision.Broadphase;
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
    [UpdateBefore(typeof(LingeringTargetedSpawnExpansionSystem))]
    public partial struct TargetedResolveSystem : ISystem
    {
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
                .WithNone<LingeringTargetedTag>()
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
        [WithNone(typeof(LingeringTargetedTag))]
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
                bool resolved = TargetedResolveCore.Resolve(
                        entity, identity, payload, ref chain, config, vfxIds, vfxSize, timing,
                        ref kinematics, DeltaTime, TargetEntities, TargetPositions, TargetShapes, TargetFactions,
                        OccupiedTargetCells, HitWriter, CircularVfxPending, TimedCircularVfxPending,
                        LineSegmentVfxPending, out int linksResolved);
                if (CollectTargetedLinks != 0 && linksResolved > 0)
                {
                    TargetedLinkCounts.Enqueue(linksResolved);
                }

                if (resolved)
                {
                    CombatDeathUtility.Kill(active, arming);
                }
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetSpatialHashSystem))]
    [UpdateAfter(typeof(CombatArmingSystem))]
    [UpdateBefore(typeof(CombatApplyFinalizeSingleSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(TargetedSpawnExpansionSystem))]
    [UpdateBefore(typeof(LingeringTargetedSpawnExpansionSystem))]
    public partial struct LingeringTargetedResolveSystem : ISystem
    {
        private EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TargetedTag>()
                .WithAll<LingeringTargetedTag>()
                .WithAll<Active>()
                .WithAllRW<TargetedChainComponent>()
                .WithAllRW<TargetedTickGateComponent>()
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

            JobHandle handle = new LingeringTargetedResolveJob
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
        [WithAll(typeof(TargetedTag), typeof(LingeringTargetedTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct LingeringTargetedResolveJob : IJobEntity
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
                ref TargetedTickGateComponent tickGate)
            {
                tickGate.Remaining -= DeltaTime;
                if (tickGate.Remaining > 0f)
                {
                    return;
                }

                chain.LinkIndex = 0;
                chain.LinkSource = chain.Origin;
                chain.LinkTarget = chain.Origin;
                chain.LinkGateRemaining = 0f;
                tickGate.Remaining += tickGate.TickIntervalSeconds;
                TargetedResolveCore.Resolve(
                    entity, identity, payload, ref chain, config, vfxIds, vfxSize, timing,
                    ref kinematics, DeltaTime, TargetEntities, TargetPositions, TargetShapes, TargetFactions,
                    OccupiedTargetCells, HitWriter, CircularVfxPending, TimedCircularVfxPending,
                    LineSegmentVfxPending, out int linksResolved);
                if (CollectTargetedLinks != 0 && linksResolved > 0)
                {
                    TargetedLinkCounts.Enqueue(linksResolved);
                }
            }
        }
    }
}
