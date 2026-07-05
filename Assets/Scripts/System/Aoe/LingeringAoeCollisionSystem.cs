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
    [UpdateAfter(typeof(AoeContactGateSystem))]
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
                .WithAll<AoeHitGateComponent>()
                .WithAll<AoeHitSpawnComponent>()
                .WithAll<AoeAreaComponent>()
                .WithAllRW<CombatRenderActiveTag>()
                .WithAllRW<AoeContactGateElement>()
                .WithPresent<CombatLifetimeComponent>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (lingeringAoeQuery.IsEmpty)
                return;

            var hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);

            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var impactAoeExpansion = state.World.GetExistingSystemManaged<ImpactAoeSpawnExpansionSystem>();
            var lingeringAoeExpansion = state.World.GetExistingSystemManaged<LingeringAoeSpawnExpansionSystem>();
            var hitApply = state.World.GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>();
            var vfx = state.World.GetExistingSystemManaged<CombatVfxDispatchSystem>();

            var job = new LingeringAoeCollisionJob
            {
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetShapes = hash.TargetShapes.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                OccupiedTargetCells = hash.AoeOccupiedCells,
                HitWriter = hitApply != null
                    ? hitApply.AsParallelWriter()
                    : default,
                HasHitWriter = hitApply != null && hitApply.HitQueue.IsCreated,
                VfxPending = vfx != null
                    ? vfx.AsParallelWriter()
                    : default,
                HasVfxWriter = vfx != null && vfx.HasQueue,
                ProjectileEventWriter = expansion != null
                    ? expansion.EventQueue.AsParallelWriter()
                    : default,
                ImpactAoeEventWriter = impactAoeExpansion != null
                    ? impactAoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                LingeringAoeEventWriter = lingeringAoeExpansion != null
                    ? lingeringAoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasImpactAoeEventWriter = impactAoeExpansion != null && impactAoeExpansion.EventQueue.IsCreated,
                HasLingeringAoeEventWriter = lingeringAoeExpansion != null && lingeringAoeExpansion.EventQueue.IsCreated
            };

            var collisionHandle = job.ScheduleParallel(lingeringAoeQuery, state.Dependency);

            if (expansion != null)
                expansion.ProducerHandle =
                    JobHandle.CombineDependencies(expansion.ProducerHandle, collisionHandle);
            if (impactAoeExpansion != null)
                impactAoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(impactAoeExpansion.ProducerHandle, collisionHandle);
            if (lingeringAoeExpansion != null)
                lingeringAoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(lingeringAoeExpansion.ProducerHandle, collisionHandle);
            if (hitApply != null)
                hitApply.ProducerHandle =
                    JobHandle.CombineDependencies(hitApply.ProducerHandle, collisionHandle);
            if (vfx != null)
                vfx.ProducerHandle =
                    JobHandle.CombineDependencies(vfx.ProducerHandle, collisionHandle);

            var rw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
            rw.ValueRW.ConsumerHandle =
                JobHandle.CombineDependencies(rw.ValueRW.ConsumerHandle, collisionHandle);
            state.Dependency = collisionHandle;
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
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;
            public bool HasHitWriter;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;
            public NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter ImpactAoeEventWriter;
            public NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter LingeringAoeEventWriter;
            public bool HasImpactAoeEventWriter;
            public bool HasLingeringAoeEventWriter;

            private void Execute(
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
