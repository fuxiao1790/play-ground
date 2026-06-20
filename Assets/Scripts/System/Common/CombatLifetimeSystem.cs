using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Common
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    [UpdateBefore(typeof(TimedProjectileSpawnSystem))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileContactGateSystem))]
    [UpdateBefore(typeof(ProjectileCollisionSystem))]
    [UpdateBefore(typeof(AoeContactGateSystem))]
    [UpdateBefore(typeof(LingeringAoeCollisionSystem))]
    public partial struct CombatLifetimeSystem : ISystem
    {
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<CombatScope>());
        }

        public void OnUpdate(ref SystemState state)
        {
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);
            float deltaTime = SystemAPI.Time.DeltaTime;
            Entity scope = scopeQuery.GetSingletonEntity();
            BufferLookup<VfxSpawnRequestElement> vfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>();

            var projectileJob = new ProjectileLifetimeJob
            {
                DeltaTime = deltaTime,
                VfxPending = vfxPending.AsParallelWriter()
            };
            var aoeJob = new AoeLifetimeJob
            {
                DeltaTime = deltaTime,
                VfxPending = vfxPending.AsParallelWriter()
            };

            // aoeJob chains after projectileJob — both write to vfxPending.AsParallelWriter()
            // and NativeQueue<T> safety doesn't permit concurrent parallel writers across jobs.
            JobHandle projectileHandle = projectileJob.ScheduleParallel(state.Dependency);
            JobHandle aoeHandle = aoeJob.ScheduleParallel(projectileHandle);

            JobHandle vfxFlushHandle = new VfxFlushJob
            {
                Scope = scope,
                Pending = vfxPending,
                VfxBuffers = vfxBuffers
            }.Schedule(aoeHandle);

            state.Dependency = vfxPending.Dispose(vfxFlushHandle);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(CombatLifetimeComponent))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

            private void Execute(
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatRenderComponent render,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    active.ValueRW = false;
                    renderActive.ValueRW = false;
                    VfxPending.Enqueue(new VfxPendingSpawn
                    {
                        Faction = identity.Faction,
                        TypeId = identity.TypeId,
                        Trigger = 2,
                        Position = kinematics.Position,
                        AreaSize = math.max(render.VisualScale.x, render.VisualScale.y)
                    });
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(CombatLifetimeComponent))]
        private partial struct AoeLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

            private void Execute(
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatRenderComponent render,
                ref CombatLifetimeComponent lifetime,
                EnabledRefRW<Active> active,
                EnabledRefRW<AoeCollisionActiveTag> collisionActive,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                lifetime.Remaining -= DeltaTime;
                if (lifetime.Remaining <= 0f)
                {
                    lifetime.Remaining = 0f;
                    active.ValueRW = false;
                    collisionActive.ValueRW = false;
                    renderActive.ValueRW = false;
                    VfxPending.Enqueue(new VfxPendingSpawn
                    {
                        Faction = identity.Faction,
                        TypeId = identity.TypeId,
                        Trigger = 2,
                        Position = kinematics.Position,
                        AreaSize = math.max(render.VisualScale.x, render.VisualScale.y)
                    });
                }
            }
        }
    }
}
