using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileChildSpawnSystem))]
    [UpdateBefore(typeof(ProjectileCollisionSystem))]
    public partial struct ProjectileLifetimeSystem : ISystem
    {
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<CombatScope>());
        }

        public void OnUpdate(ref SystemState state)
        {
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);
            var job = new ProjectileLifetimeJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                VfxPending = vfxPending.AsParallelWriter()
            };

            JobHandle lifetimeHandle = job.ScheduleParallel(state.Dependency);
            JobHandle vfxFlushHandle = new VfxFlushJob
            {
                Scope = scopeQuery.GetSingletonEntity(),
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(lifetimeHandle);

            state.Dependency = vfxPending.Dispose(vfxFlushHandle);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

            private void Execute(
                Entity entity,
                ref ProjectileLifetimeComponent lifetime,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatRenderComponent render,
                EnabledRefRW<ProjectileActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                lifetime.RemainingLifetime -= DeltaTime;
                if (lifetime.RemainingLifetime <= 0f)
                {
                    lifetime.RemainingLifetime = 0f;
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
    }
}
