using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CombatApplyFinalizeSingleSystem))]
    public partial struct LingeringAoeCollisionSystem : ISystem
    {
        private EntityQuery lingeringAoeQuery;

        public void OnCreate(ref SystemState state)
        {
            lingeringAoeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<Active>()
                .WithAll<AoeCollisionActiveTag>()
                .WithAll<AoeIdentityComponent>()
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatCollisionComponent>()
                .WithAllRW<AoeHitGateComponent>()
                .WithAll<AoeHitSpawnComponent>()
                .WithAll<AoeAreaComponent>()
                .WithAllRW<CombatRenderActiveTag>()
                .WithAll<LingeringAoeTag>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (lingeringAoeQuery.IsEmpty)
                return;

            var hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);

            bool hasProjectileEvents = SystemAPI.TryGetSingletonRW<ProjectileSpawnEventSingleton>(
                out RefRW<ProjectileSpawnEventSingleton> projectileLane);
            NativeQueue<ProjectileSpawnEvent> projectileEventQueue =
                hasProjectileEvents ? projectileLane.ValueRO.EventQueue : default;
            hasProjectileEvents = hasProjectileEvents && projectileEventQueue.IsCreated;

            bool hasImpactAoeEvents = SystemAPI.TryGetSingletonRW<ImpactAoeSpawnEventSingleton>(
                out RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane);
            NativeQueue<ImpactAoeSpawnEvent> impactAoeEventQueue =
                hasImpactAoeEvents ? impactAoeLane.ValueRO.EventQueue : default;
            hasImpactAoeEvents = hasImpactAoeEvents && impactAoeEventQueue.IsCreated;

            bool hasLingeringAoeEvents = SystemAPI.TryGetSingletonRW<LingeringAoeSpawnEventSingleton>(
                out RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane);
            NativeQueue<LingeringAoeSpawnEvent> lingeringAoeEventQueue =
                hasLingeringAoeEvents ? lingeringAoeLane.ValueRO.EventQueue : default;
            hasLingeringAoeEvents = hasLingeringAoeEvents && lingeringAoeEventQueue.IsCreated;

            bool hasHit = SystemAPI.TryGetSingletonRW<CombatHitDispatchSingleton>(
                out RefRW<CombatHitDispatchSingleton> hitDispatch);
            NativeQueue<CombatHitEvent> hitQueue = hasHit ? hitDispatch.ValueRO.HitQueue : default;
            hasHit = hasHit && hitQueue.IsCreated;

            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatVfxDispatchSingleton>(
                out RefRW<CombatVfxDispatchSingleton> vfx);
            NativeQueue<VfxPendingSpawn> vfxQueue = hasVfx ? vfx.ValueRO.PendingSpawns : default;
            hasVfx = hasVfx && vfxQueue.IsCreated;

            var job = new LingeringAoeCollisionJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetShapes = hash.TargetShapes.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                OccupiedTargetCells = hash.AoeOccupiedCells,
                HitWriter = hasHit
                    ? hitQueue.AsParallelWriter()
                    : default,
                HasHitWriter = hasHit,
                VfxPending = hasVfx
                    ? vfxQueue.AsParallelWriter()
                    : default,
                HasVfxWriter = hasVfx,
                ProjectileEventWriter = hasProjectileEvents
                    ? projectileEventQueue.AsParallelWriter()
                    : default,
                ImpactAoeEventWriter = hasImpactAoeEvents
                    ? impactAoeEventQueue.AsParallelWriter()
                    : default,
                LingeringAoeEventWriter = hasLingeringAoeEvents
                    ? lingeringAoeEventQueue.AsParallelWriter()
                    : default,
                HasImpactAoeEventWriter = hasImpactAoeEvents,
                HasLingeringAoeEventWriter = hasLingeringAoeEvents
            };

            var collisionHandle = job.ScheduleParallel(lingeringAoeQuery, state.Dependency);

            if (hasProjectileEvents)
                projectileLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, collisionHandle);
            if (hasImpactAoeEvents)
                impactAoeLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, collisionHandle);
            if (hasLingeringAoeEvents)
                lingeringAoeLane.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, collisionHandle);
            if (hasHit)
                hitDispatch.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(hitDispatch.ValueRW.ProducerHandle, collisionHandle);
            if (hasVfx)
                vfx.ValueRW.ProducerHandle =
                    JobHandle.CombineDependencies(vfx.ValueRW.ProducerHandle, collisionHandle);

            var rw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
            rw.ValueRW.ConsumerHandle =
                JobHandle.CombineDependencies(rw.ValueRW.ConsumerHandle, collisionHandle);
            state.Dependency = collisionHandle;
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(AoeCollisionActiveTag))]
        private partial struct LingeringAoeCollisionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public bool HasHitWriter;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public float DeltaTime;
            public bool HasImpactAoeEventWriter;
            public bool HasLingeringAoeEventWriter;

            private void Execute(
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                ref AoeHitGateComponent hitGate,
                in AoeHitSpawnComponent hitSpawn,
                in AoeAreaComponent area,
                EnabledRefRW<Active> active,
                EnabledRefRW<AoeCollisionActiveTag> collisionActive,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                hitGate.Remaining -= DeltaTime;
                if (hitGate.Remaining > 0f)
                {
                    return;
                }

                hitGate.Remaining = hitGate.RepeatHitCooldownSeconds > 0f
                    ? hitGate.Remaining + hitGate.RepeatHitCooldownSeconds
                    : 0f;

                AoeCollisionCore.RunCollision(
                    identity,
                    kinematics,
                    collision,
                    hitSpawn,
                    area,
                    false,
                    active,
                    collisionActive,
                    renderActive,
                    TargetEntities,
                    TargetPositions,
                    TargetShapes,
                    TargetFactions,
                    OccupiedTargetCells,
                    HitWriter,
                    HasHitWriter,
                    VfxPending,
                    HasVfxWriter,
                    ProjectileEventWriter,
                    ImpactAoeEventWriter,
                    LingeringAoeEventWriter,
                    HasImpactAoeEventWriter,
                    HasLingeringAoeEventWriter);
            }
        }
    }
}
