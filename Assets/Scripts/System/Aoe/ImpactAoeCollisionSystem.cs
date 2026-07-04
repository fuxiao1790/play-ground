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
                .WithAll<AoeCollisionActiveTag>()
                .WithAll<AoeIdentityComponent>()
                .WithAll<CombatKinematicsComponent>()
                .WithAll<CombatCollisionComponent>()
                .WithAll<AoeHitGateComponent>()
                .WithAll<AoeHitSpawnComponent>()
                .WithAll<AoeAreaComponent>()
                .WithAllRW<CombatRenderActiveTag>()
                .WithNone<CombatLifetimeComponent>()
                .Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (impactAoeQuery.IsEmpty)
                return;

            var hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);

            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            var hitApply = state.World.GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>();
            var vfx = state.World.GetExistingSystemManaged<CombatVfxDispatchSystem>();

            var job = new ImpactAoeCollisionJob
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
                AoeEventWriter = aoeExpansion != null
                    ? aoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasAoeEventWriter = aoeExpansion != null && aoeExpansion.EventQueue.IsCreated
            };

            var collisionHandle = job.ScheduleParallel(impactAoeQuery, state.Dependency);

            if (expansion != null)
                expansion.ProducerHandle =
                    JobHandle.CombineDependencies(expansion.ProducerHandle, collisionHandle);
            if (aoeExpansion != null)
                aoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, collisionHandle);
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
        [WithNone(typeof(CombatLifetimeComponent))]
        private partial struct ImpactAoeCollisionJob : IJobEntity
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
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventWriter;
            public bool HasAoeEventWriter;

            private void Execute(
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in AoeHitGateComponent hitGate,
                in AoeHitSpawnComponent hitSpawn,
                in AoeAreaComponent area,
                EnabledRefRW<Active> active,
                EnabledRefRW<AoeCollisionActiveTag> collisionActive,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                var gate = new ScratchGate { Seen = default };
                AoeCollisionCore.RunCollision(
                    identity,
                    kinematics,
                    collision,
                    hitSpawn,
                    area,
                    0f,
                    true,
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
                    AoeEventWriter,
                    HasAoeEventWriter);
            }
        }
    }
}
