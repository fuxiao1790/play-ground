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
using Unity.Burst.Intrinsics;
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
                EntityHandle = SystemAPI.GetEntityTypeHandle(),
                IdentityHandle = SystemAPI.GetComponentTypeHandle<AoeIdentityComponent>(true),
                PayloadHandle = SystemAPI.GetComponentTypeHandle<CombatHitPayload>(true),
                KinematicsHandle = SystemAPI.GetComponentTypeHandle<CombatKinematicsComponent>(true),
                CollisionHandle = SystemAPI.GetComponentTypeHandle<CombatCollisionComponent>(true),
                HitGateHandle = SystemAPI.GetComponentTypeHandle<AoeHitGateComponent>(false),
                HitSpawnHandle = SystemAPI.GetComponentTypeHandle<AoeHitSpawnComponent>(true),
                TimedSpawnHandle = SystemAPI.GetComponentTypeHandle<TimedSpawnComponent>(true),
                VfxIdsHandle = SystemAPI.GetComponentTypeHandle<AoeVfxIds>(true),
                TimingHandle = SystemAPI.GetComponentTypeHandle<VfxTimingData>(true),
                AreaHandle = SystemAPI.GetComponentTypeHandle<AoeAreaComponent>(true),
                ActiveHandle = SystemAPI.GetComponentTypeHandle<Active>(false),
                CollisionActiveHandle =
                    SystemAPI.GetComponentTypeHandle<CombatCollisionActiveTag>(false),
                ArmingHandle = SystemAPI.GetComponentTypeHandle<ArmingTag>(false),
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
        private struct LingeringAoeCollisionJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<AoeIdentityComponent> IdentityHandle;
            [ReadOnly] public ComponentTypeHandle<CombatHitPayload> PayloadHandle;
            [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [ReadOnly] public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<AoeHitGateComponent> HitGateHandle;
            [ReadOnly] public ComponentTypeHandle<AoeHitSpawnComponent> HitSpawnHandle;
            [ReadOnly] public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            [ReadOnly] public ComponentTypeHandle<AoeVfxIds> VfxIdsHandle;
            [ReadOnly] public ComponentTypeHandle<VfxTimingData> TimingHandle;
            [ReadOnly] public ComponentTypeHandle<AoeAreaComponent> AreaHandle;
            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<CombatCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<ArmingTag> ArmingHandle;
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

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityHandle);
                NativeArray<AoeIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                NativeArray<CombatHitPayload> payloads = chunk.GetNativeArray(ref PayloadHandle);
                NativeArray<CombatKinematicsComponent> kinematics =
                    chunk.GetNativeArray(ref KinematicsHandle);
                NativeArray<CombatCollisionComponent> collisions =
                    chunk.GetNativeArray(ref CollisionHandle);
                NativeArray<AoeHitGateComponent> hitGates = chunk.GetNativeArray(ref HitGateHandle);
                NativeArray<AoeHitSpawnComponent> hitSpawns = chunk.GetNativeArray(ref HitSpawnHandle);
                NativeArray<TimedSpawnComponent> timedSpawns = chunk.GetNativeArray(ref TimedSpawnHandle);
                NativeArray<AoeVfxIds> vfxIds = chunk.GetNativeArray(ref VfxIdsHandle);
                NativeArray<VfxTimingData> timings = chunk.GetNativeArray(ref TimingHandle);
                NativeArray<AoeAreaComponent> areas = chunk.GetNativeArray(ref AreaHandle);
                EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingHandle);

                using NativeArray<int> seenTargetKeys = new NativeArray<int>(
                    CollisionConstants.MaxAoeTargetsPerTick,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);

                ChunkEntityEnumerator enumerator =
                    new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    AoeHitGateComponent hitGate = hitGates[i];
                    hitGate.Remaining -= DeltaTime;
                    if (hitGate.Remaining > 0f)
                    {
                        hitGates[i] = hitGate;
                        continue;
                    }

                    hitGate.Remaining = hitGate.RepeatHitCooldownSeconds > 0f
                        ? hitGate.Remaining + hitGate.RepeatHitCooldownSeconds
                        : 0f;
                    hitGates[i] = hitGate;

                    AoeCollisionCore.RunCollision(
                        entities[i],
                        identities[i],
                        payloads[i],
                        kinematics[i],
                        collisions[i],
                        hitSpawns[i],
                        timedSpawns[i],
                        vfxIds[i],
                        timings[i],
                        areas[i],
                        false,
                        activeMask.GetEnabledRefRW<Active>(i),
                        collisionActiveMask.GetEnabledRefRW<CombatCollisionActiveTag>(i),
                        armingMask.GetEnabledRefRW<ArmingTag>(i),
                        TargetEntities,
                        TargetPositions,
                        TargetShapes,
                        TargetFactions,
                        OccupiedTargetCells,
                        seenTargetKeys,
                        0,
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
}
