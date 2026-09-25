using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Combat.Projectiles
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileContactGateSystem))]
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
        [WithDisabled(typeof(ArmingTag))]
        private partial struct ProjectileMovementJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref CombatKinematicsComponent kinematics,
                ref CombatCollisionComponent collision)
            {
                kinematics.Position += kinematics.Velocity * DeltaTime;
                CombatCollisionMath.ComputeWorldBounds(
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
