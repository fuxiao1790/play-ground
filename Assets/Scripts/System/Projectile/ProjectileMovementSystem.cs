using PlayGround.System.Common;
using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(PlayGround.System.Common.CombatRenderPrepareSystem))]
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
        [WithAll(typeof(ProjectileTag), typeof(Active))]
        private partial struct ProjectileMovementJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref CombatKinematicsComponent kinematics,
                ref CombatCollisionComponent collision)
            {
                kinematics.Position += kinematics.Velocity * DeltaTime;
                ProjectileCollisionMath.ComputeWorldBounds(
                    kinematics.Position,
                    collision.Radius,
                    collision.HalfExtents,
                    collision.RotationRadians,
                    collision.ShapeType,
                    out collision.BoundsMin,
                    out collision.BoundsMax);
            }
        }
    }
}
