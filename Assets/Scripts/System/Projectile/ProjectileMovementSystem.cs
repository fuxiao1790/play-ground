using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileTrackingSystem))]
    public partial struct ProjectileMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new ProjectileMovementJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileMovementJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(ref ProjectileComponent projectile)
            {
                projectile.Position += projectile.Velocity * DeltaTime;
                ProjectileCollisionMath.ComputeWorldBounds(
                    projectile.Position,
                    projectile.Radius,
                    projectile.HalfExtents,
                    projectile.RotationRadians,
                    projectile.ShapeType,
                    out projectile.BoundsMin,
                    out projectile.BoundsMax);
            }
        }
    }
}
