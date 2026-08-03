using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Combat.Projectiles
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ProjectileMovementSystem))]
    public partial struct SweptProjectileOriginSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new CaptureOriginJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(SweptProjectileTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct CaptureOriginJob : IJobEntity
        {
            private void Execute(
                in CombatKinematicsComponent kinematics,
                ref ProjectileSweepComponent sweep)
            {
                sweep.Origin = kinematics.Position;
            }
        }
    }
}
