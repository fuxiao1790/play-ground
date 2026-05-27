using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    public partial struct ProjectileDespawnSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton =
                SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var job = new ProjectileDespawnJob
            {
                CommandBuffer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileExpiredTag))]
        private partial struct ProjectileDespawnJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity)
            {
                CommandBuffer.DestroyEntity(chunkIndex, entity);
            }
        }
    }
}
