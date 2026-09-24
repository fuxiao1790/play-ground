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
                .WithAll<CombatHitPayload>()
                .WithAll<AoeAreaComponent>()
                .WithAll<AoeVfxIds>()
                .WithAll<VfxTimingData>()
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

            // Hit dispatch and the VFX lane are created unconditionally by their owning systems'
            // OnCreate. Read them directly: a missing lane is a broken world and must throw here,
            // not be silently skipped.
            RefRW<CombatHitDispatchSingleton> hitDispatch =
                SystemAPI.GetSingletonRW<CombatHitDispatchSingleton>();
            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();

            var job = new ImpactAoeCollisionJob
            {
                EntityHandle = SystemAPI.GetEntityTypeHandle(),
                IdentityHandle = SystemAPI.GetComponentTypeHandle<AoeIdentityComponent>(true),
                PayloadHandle = SystemAPI.GetComponentTypeHandle<CombatHitPayload>(true),
                KinematicsHandle = SystemAPI.GetComponentTypeHandle<CombatKinematicsComponent>(true),
                CollisionHandle = SystemAPI.GetComponentTypeHandle<CombatCollisionComponent>(true),
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
                SpawnTemplateDeltas =
                    SystemAPI.GetSingleton<SpawnTemplateRegistryState>().Deltas.AsParallelWriter()
            };

            var collisionHandle = job.ScheduleParallel(impactAoeQuery, state.Dependency);

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
        private struct ImpactAoeCollisionJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<AoeIdentityComponent> IdentityHandle;
            [ReadOnly] public ComponentTypeHandle<CombatHitPayload> PayloadHandle;
            [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            [ReadOnly] public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
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
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter SpawnTemplateDeltas;

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
                    AoeCollisionCore.RunCollision(
                        entities[i],
                        identities[i],
                        payloads[i],
                        kinematics[i],
                        collisions[i],
                        // Impact archetypes carry no TimedSpawnComponent.
                        default,
                        vfxIds[i],
                        timings[i],
                        areas[i],
                        true,
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
                        SpawnTemplateDeltas);
                }
            }
        }
    }
}
