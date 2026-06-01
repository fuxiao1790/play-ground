using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileChildSpawnSystem))]
    [UpdateBefore(typeof(ProjectileCollisionSystem))]
    public partial struct ProjectileLifetimeSystem : ISystem
    {
        private static readonly ProfilerMarker DespawnFrameTimeProfilerMarker =
            new("Projectile.Despawn.Lifetime.FrameTime");

        public void OnUpdate(ref SystemState state)
        {
            using (DespawnFrameTimeProfilerMarker.Auto())
            {
                var recycled = new NativeQueue<ProjectilePendingRecycle>(Allocator.TempJob);
                var job = new ProjectileLifetimeJob
                {
                    DeltaTime = SystemAPI.Time.DeltaTime,
                    ChildSpawnerTags = SystemAPI.GetComponentLookup<ProjectileChildSpawnerTag>(true),
                    Recycled = recycled.AsParallelWriter()
                };

                JobHandle lifetimeHandle = job.ScheduleParallel(state.Dependency);
                JobHandle flushHandle = new ProjectileRecycleFlushJob
                {
                    Recycled = recycled,
                    RecycleBuffers = SystemAPI.GetBufferLookup<ProjectileRecycleElement>()
                }.Schedule(lifetimeHandle);

                state.Dependency = recycled.Dispose(flushHandle);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public ComponentLookup<ProjectileChildSpawnerTag> ChildSpawnerTags;
            public NativeQueue<ProjectilePendingRecycle>.ParallelWriter Recycled;

            private void Execute(
                Entity entity,
                ref ProjectileLifetimeComponent lifetime,
                in ProjectileIdentityComponent identity,
                EnabledRefRW<ProjectileActiveTag> active)
            {
                lifetime.RemainingLifetime -= DeltaTime;
                if (lifetime.RemainingLifetime <= 0f)
                {
                    lifetime.RemainingLifetime = 0f;
                    active.ValueRW = false;
                    Recycled.Enqueue(new ProjectilePendingRecycle
                    {
                        Scope = identity.Scope,
                        ProjectileEntity = entity,
                        TypeId = identity.TypeId,
                        HasChildSpawner = ChildSpawnerTags.HasComponent(entity) ? 1 : 0
                    });
                }
            }
        }
    }
}
