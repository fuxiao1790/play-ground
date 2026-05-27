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
            var job = new ProjectileLifetimeJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileLifetimeJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref ProjectileComponent projectile,
                EnabledRefRW<ProjectileActiveTag> active)
            {
                projectile.RemainingLifetime -= DeltaTime;
                if (projectile.RemainingLifetime <= 0f)
                {
                    projectile.RemainingLifetime = 0f;
                    active.ValueRW = false;
                }
            }
        }
    }
}
