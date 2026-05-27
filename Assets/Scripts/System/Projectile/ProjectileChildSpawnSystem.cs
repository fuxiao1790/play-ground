using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileLifetimeSystem))]
    public partial struct ProjectileChildSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton =
                SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var job = new ProjectileChildSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                CommandBuffer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileChildSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            private void Execute([ChunkIndexInQuery] int chunkIndex, ref ProjectileComponent projectile)
            {
                if (projectile.RemainingLifetime <= 0f
                    || projectile.Scope == Entity.Null
                    || projectile.ChildSpawnerId <= 0
                    || projectile.ChildSpawnIntervalSeconds <= 0f)
                {
                    return;
                }

                float cooldown = projectile.ChildSpawnCooldownRemaining - DeltaTime;
                int tickIndex = projectile.ChildSpawnTickIndex;
                while (cooldown <= 0f)
                {
                    tickIndex++;
                    CommandBuffer.AppendToBuffer(chunkIndex, projectile.Scope, new ProjectileChildSpawnRequestElement
                    {
                        ProjectileId = projectile.ProjectileId,
                        ProjectileTypeId = projectile.TypeId,
                        ChildSpawnerId = projectile.ChildSpawnerId,
                        TickIndex = tickIndex,
                        Position = projectile.Position,
                        Velocity = projectile.Velocity,
                        DamageAmount = projectile.DamageAmount,
                        Order = ProjectileEventOrder.ForProjectileTarget(projectile.ProjectileId, tickIndex)
                    });
                    cooldown += projectile.ChildSpawnIntervalSeconds;
                }

                projectile.ChildSpawnCooldownRemaining = cooldown;
                projectile.ChildSpawnTickIndex = tickIndex;
            }
        }
    }
}
