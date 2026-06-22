using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeContactGateSystem))]
    [UpdateBefore(typeof(HitApplyFinalizeSystem))]
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
    public partial struct LingeringAoeCollisionSystem : ISystem
    {
        private EntityQuery lingeringAoeQuery;
        private EntityQuery targetQuery;

        public void OnCreate(ref SystemState state)
        {
            lingeringAoeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<Active>()
                .WithAll<AoeCollisionActiveTag>()
                .WithAll<AoeIdentityComponent>()
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatCollisionComponent>()
                .WithAll<AoeHitGateComponent>()
                .WithAll<AoeHitSpawnComponent>()
                .WithAll<AoeAreaComponent>()
                .WithAllRW<CombatRenderActiveTag>()
                .WithAllRW<AoeContactGateElement>()
                .WithPresent<CombatLifetimeComponent>()
                .Build(ref state);
            targetQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<TargetProxyTag>(),
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadOnly<TargetCollisionShape>(),
                ComponentType.ReadOnly<TargetFaction>());
        }

        public void OnUpdate(ref SystemState state)
        {
            int lingeringAoeCount = lingeringAoeQuery.CalculateEntityCount();
            if (lingeringAoeCount == 0)
                return;

            if (!SystemAPI.TryGetSingletonEntity<CombatScope>(out Entity combatScope))
                return;

            state.EntityManager.CompleteDependencyBeforeRO<TargetPosition>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetCollisionShape>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetFaction>();

            NativeArray<Entity> targetEntities = targetQuery.ToEntityArray(Allocator.TempJob);
            NativeArray<TargetPosition> targetPositions = targetQuery.ToComponentDataArray<TargetPosition>(Allocator.TempJob);
            NativeArray<TargetCollisionShape> targetShapes = targetQuery.ToComponentDataArray<TargetCollisionShape>(Allocator.TempJob);
            NativeArray<TargetFaction> targetFactions = targetQuery.ToComponentDataArray<TargetFaction>(Allocator.TempJob);

            int targetCellCapacity = 0;
            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCollisionShape target = targetShapes[i];
                int2 min = AoeCollisionCore.MinCell(target.BoundsMin);
                int2 max = AoeCollisionCore.MaxCell(target.BoundsMax);
                targetCellCapacity += ((max.x - min.x) + 1) * ((max.y - min.y) + 1);
            }

            var occupiedTargetCells = new NativeParallelMultiHashMap<long, int>(
                math.max(1, targetCellCapacity), Allocator.TempJob);
            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCollisionShape target = targetShapes[i];
                int2 min = AoeCollisionCore.MinCell(target.BoundsMin);
                int2 max = AoeCollisionCore.MaxCell(target.BoundsMax);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                    occupiedTargetCells.Add(AoeCollisionCore.CellKey(targetFactions[i].Value, x, y), i);
            }

            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            var hitApply = state.World.GetExistingSystemManaged<HitApplyFinalizeSystem>();
            var vfxPending = new NativeStream(lingeringAoeCount, Allocator.TempJob);

            var job = new LingeringAoeCollisionJob
            {
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetShapes = targetShapes,
                OccupiedTargetCells = occupiedTargetCells,
                HitWriter = hitApply != null
                    ? hitApply.AsParallelWriter()
                    : default,
                HasHitWriter = hitApply != null && hitApply.HitMap.IsCreated,
                VfxPending = vfxPending.AsWriter(),
                ProjectileEventWriter = expansion != null
                    ? expansion.EventQueue.AsParallelWriter()
                    : default,
                AoeEventWriter = aoeExpansion != null
                    ? aoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasAoeEventWriter = aoeExpansion != null && aoeExpansion.EventQueue.IsCreated
            };

            // Pass lingeringAoeQuery explicitly so [EntityIndexInQuery] stays in [0, lingeringAoeCount)
            // and matches the NativeStream size exactly.
            var collisionHandle = job.ScheduleParallel(lingeringAoeQuery, state.Dependency);

            if (expansion != null)
                expansion.ProducerHandle =
                    JobHandle.CombineDependencies(expansion.ProducerHandle, collisionHandle);
            if (aoeExpansion != null)
                aoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, collisionHandle);
            if (hitApply != null)
                hitApply.ProducerHandle =
                    JobHandle.CombineDependencies(hitApply.ProducerHandle, collisionHandle);

            var vfxFlushHandle = new VfxStreamFlushJob
            {
                Scope = combatScope,
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            JobHandle targetDisposeHandle = JobHandle.CombineDependencies(
                targetEntities.Dispose(collisionHandle),
                JobHandle.CombineDependencies(
                    targetPositions.Dispose(collisionHandle),
                    JobHandle.CombineDependencies(
                        targetShapes.Dispose(collisionHandle),
                        targetFactions.Dispose(collisionHandle))));
            state.Dependency = occupiedTargetCells.Dispose(
                JobHandle.CombineDependencies(targetDisposeHandle, disposeVfxHandle));
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(AoeCollisionActiveTag))]
        // Pulse AOEs have CombatLifetimeComponent DISABLED (not absent). WithPresent
        // includes them so the job can deactivate them after a single pass.
        [WithPresent(typeof(CombatLifetimeComponent))]
        private partial struct LingeringAoeCollisionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeParallelMultiHashMap<Entity, CombatHitEvent>.ParallelWriter HitWriter;
            public bool HasHitWriter;
            public NativeStream.Writer VfxPending;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventWriter;
            public bool HasAoeEventWriter;

            private void Execute(
                [EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                EnabledRefRO<CombatLifetimeComponent> lifetimeEnabled,
                in AoeHitGateComponent hitGate,
                in AoeHitSpawnComponent hitSpawn,
                in AoeAreaComponent area,
                EnabledRefRW<Active> active,
                EnabledRefRW<AoeCollisionActiveTag> collisionActive,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                var gate = new BufferGate { ContactGates = contactGates };
                bool enabledLifetime = lifetimeEnabled.ValueRO;
                AoeCollisionCore.RunCollision(
                    entityIndexInQuery,
                    identity,
                    kinematics,
                    collision,
                    hitSpawn,
                    area,
                    enabledLifetime ? hitGate.RepeatHitCooldownSeconds : 0f,
                    !enabledLifetime,
                    active,
                    collisionActive,
                    renderActive,
                    ref gate,
                    TargetEntities,
                    TargetPositions,
                    TargetShapes,
                    OccupiedTargetCells,
                    HitWriter,
                    HasHitWriter,
                    VfxPending,
                    ProjectileEventWriter,
                    AoeEventWriter,
                    HasAoeEventWriter);
            }
        }
    }
}
