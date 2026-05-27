using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileChildSpawnSystem))]
    [UpdateBefore(typeof(ProjectileCollisionSystem))]
    public partial struct ProjectileLifetimeSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton =
                SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var job = new ProjectileLifetimeJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                CommandBuffer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithNone(typeof(ProjectileExpiredTag))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref ProjectileComponent projectile)
            {
                projectile.RemainingLifetime -= DeltaTime;
                if (projectile.RemainingLifetime <= 0f)
                {
                    projectile.RemainingLifetime = 0f;
                    CommandBuffer.AddComponent<ProjectileExpiredTag>(chunkIndex, entity);
                }
            }
        }
    }
}
