using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Aoes
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateBefore(typeof(CombatApplyFinalizeSingleSystem))]
    public partial struct ImpactAoeCollisionSystem : ISystem
    {
        private EntityQuery impactAoeQuery;

        public void OnCreate(ref SystemState state)
        {
            impactAoeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<Active>()
                .WithAll<CombatCollisionActiveTag>()
                .WithAll<AoeIdentityComponent>()
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatCollisionComponent>()
                .WithAll<AoeHitGateComponent>()
                .WithAll<AoeHitSpawnComponent>()
                .WithAll<CombatHitPayload>()
                .WithAll<AoeAreaComponent>()
                .WithDisabled<ArmingTag>()
                .WithNone<LingeringAoeTag>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (impactAoeQuery.IsEmpty)
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

            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatAoeVfxDispatchSingleton>(
                out RefRW<CombatAoeVfxDispatchSingleton> vfx);
            NativeQueue<AoeVfxSpawnRequest> vfxQueue = hasVfx ? vfx.ValueRO.PendingAoeSpawns : default;
            hasVfx = hasVfx && vfxQueue.IsCreated;

            var job = new ImpactAoeCollisionJob
            {
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

            var collisionHandle = job.ScheduleParallel(impactAoeQuery, state.Dependency);

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
        [WithAll(typeof(AoeTag), typeof(Active), typeof(CombatCollisionActiveTag))]
        [WithDisabled(typeof(ArmingTag))]
        [WithNone(typeof(LingeringAoeTag))]
        private partial struct ImpactAoeCollisionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public bool HasHitWriter;
            public NativeQueue<AoeVfxSpawnRequest>.ParallelWriter VfxPending;
            public bool HasVfxWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public bool HasImpactAoeEventWriter;
            public bool HasLingeringAoeEventWriter;

            private void Execute(
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatHitPayload payload,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in AoeHitSpawnComponent hitSpawn,
                in AoeAreaComponent area,
                EnabledRefRW<Active> active,
                EnabledRefRW<CombatCollisionActiveTag> collisionActive,
                EnabledRefRW<ArmingTag> arming)
            {
                AoeCollisionCore.RunCollision(
                    entity,
                    identity,
                    payload,
                    kinematics,
                    collision,
                    hitSpawn,
                    area,
                    true,
                    active,
                    collisionActive,
                    arming,
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
