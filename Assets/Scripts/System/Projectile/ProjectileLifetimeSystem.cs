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

        public void OnUpdate(ref SystemState state)
        {
            var recycled = new NativeQueue<ProjectilePendingRecycle>(Allocator.TempJob);
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);
            var job = new ProjectileLifetimeJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ChildSpawnerTags = SystemAPI.GetComponentLookup<ProjectileChildSpawnerTag>(true),
                Recycled = recycled.AsParallelWriter(),
                VfxPending = vfxPending.AsParallelWriter()
            };

            JobHandle lifetimeHandle = job.ScheduleParallel(state.Dependency);
            JobHandle recycleFlushHandle = new ProjectileRecycleFlushJob
            {
                Recycled = recycled,
                RecycleBuffers = SystemAPI.GetBufferLookup<ProjectileRecycleElement>()
            }.Schedule(lifetimeHandle);
            JobHandle vfxFlushHandle = new VfxFlushJob
            {
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(lifetimeHandle);

            JobHandle bothFlushes = JobHandle.CombineDependencies(recycleFlushHandle, vfxFlushHandle);
            state.Dependency = JobHandle.CombineDependencies(
                recycled.Dispose(bothFlushes),
                vfxPending.Dispose(bothFlushes));
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public ComponentLookup<ProjectileChildSpawnerTag> ChildSpawnerTags;
            public NativeQueue<ProjectilePendingRecycle>.ParallelWriter Recycled;
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
                    Recycled.Enqueue(new ProjectilePendingRecycle
                    {
                        Scope = identity.Scope,
                        ProjectileEntity = entity,
                        TypeId = identity.TypeId,
                        HasChildSpawner = ChildSpawnerTags.HasComponent(entity) ? 1 : 0
                    });
                    VfxPending.Enqueue(new VfxPendingSpawn
                    {
                        Scope = identity.Scope,
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
