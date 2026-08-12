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
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Aoes
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
                .WithAll<CombatCollisionActiveTag>()
                .WithAll<AoeIdentityComponent>()
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatCollisionComponent>()
                .WithAllRW<AoeHitGateComponent>()
                .WithAll<AoeHitSpawnComponent>()
                .WithAll<CombatHitPayload>()
                .WithAll<AoeAreaComponent>()
                .WithAll<AoeVfxIds>()
                .WithAll<VfxTimingData>()
                .WithDisabled<ArmingTag>()
                .WithAll<LingeringAoeTag>()
                // Read only to release its template key on death. Present, not All: the
                // component is enableable and disabled on non-timed lingering AOEs, which
                // must still collide. This job schedules with this explicit query, so the
                // job's [WithPresent] attribute does not apply here and the entry must be
                // declared on the builder too.
                .WithPresent<TimedSpawnComponent>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (lingeringAoeQuery.IsEmpty)
                return;

            var hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);

            // Spawn lanes, hit dispatch and the VFX lane are all created unconditionally by their
            // owning systems' OnCreate. Read them directly: a missing lane is a broken world and
            // must throw here, not be silently skipped.
            RefRW<ProjectileSpawnEventSingleton> projectileLane =
                SystemAPI.GetSingletonRW<ProjectileSpawnEventSingleton>();
            RefRW<ImpactAoeSpawnEventSingleton> impactAoeLane =
                SystemAPI.GetSingletonRW<ImpactAoeSpawnEventSingleton>();
            RefRW<LingeringAoeSpawnEventSingleton> lingeringAoeLane =
                SystemAPI.GetSingletonRW<LingeringAoeSpawnEventSingleton>();
            RefRW<TargetedSpawnEventSingleton> targetedLane =
                SystemAPI.GetSingletonRW<TargetedSpawnEventSingleton>();
            RefRW<CombatHitDispatchSingleton> hitDispatch =
                SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();

            var job = new LingeringAoeCollisionJob
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
                ProjectileEventWriter = projectileLane.ValueRO.EventQueue.AsParallelWriter(),
                ImpactAoeEventWriter = impactAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                LingeringAoeEventWriter = lingeringAoeLane.ValueRO.EventQueue.AsParallelWriter(),
                TargetedEventWriter = targetedLane.ValueRO.EventQueue.AsParallelWriter(),
                SpawnTemplateDeltas =
                    SystemAPI.GetSingleton<SpawnTemplateRegistryState>().Deltas.AsParallelWriter()
            };

            var collisionHandle = job.ScheduleParallel(lingeringAoeQuery, state.Dependency);

            projectileLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(projectileLane.ValueRW.ProducerHandle, collisionHandle);
            impactAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(impactAoeLane.ValueRW.ProducerHandle, collisionHandle);
            lingeringAoeLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(lingeringAoeLane.ValueRW.ProducerHandle, collisionHandle);
            targetedLane.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(targetedLane.ValueRW.ProducerHandle, collisionHandle);
            hitDispatch.ValueRW.ProducerHandle =
                JobHandle.CombineDependencies(hitDispatch.ValueRW.ProducerHandle, collisionHandle);
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
        // Read only to release its template key on death. Enableable and disabled on
        // non-timed lingering AOEs, so Present rather than All or the query would drop them.
        [WithPresent(typeof(TimedSpawnComponent))]
        private partial struct LingeringAoeCollisionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public NativeQueue<ImpactCircleVfxEvent>.ParallelWriter CircularVfxPending;
            public NativeQueue<LingeringCircleVfxEvent>.ParallelWriter TimedCircularVfxPending;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public NativeQueue<TargetedSpawnEvent>.ParallelWriter TargetedEventWriter;
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter SpawnTemplateDeltas;
            public float DeltaTime;

            private void Execute(
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatHitPayload payload,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                ref AoeHitGateComponent hitGate,
                in AoeHitSpawnComponent hitSpawn,
                in TimedSpawnComponent timedSpawn,
                in AoeVfxIds vfxIds,
                in VfxTimingData timing,
                in AoeAreaComponent area,
                EnabledRefRW<Active> active,
                EnabledRefRW<CombatCollisionActiveTag> collisionActive,
                EnabledRefRW<ArmingTag> arming)
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
                    entity,
                    identity,
                    payload,
                    kinematics,
                    collision,
                    hitSpawn,
                    timedSpawn,
                    vfxIds,
                    timing,
                    area,
                    false,
                    active,
                    collisionActive,
                    arming,
                    TargetEntities,
                    TargetPositions,
                    TargetShapes,
                    TargetFactions,
                    OccupiedTargetCells,
                    HitWriter,
                    CircularVfxPending,
                    TimedCircularVfxPending,
                    ProjectileEventWriter,
                    ImpactAoeEventWriter,
                    LingeringAoeEventWriter,
                    TargetedEventWriter,
                    SpawnTemplateDeltas);
            }
        }
    }
}
