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
    public partial struct ProjectileContinuousOriginSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new CaptureOriginJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileContinuousTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct CaptureOriginJob : IJobEntity
        {
            private void Execute(
                in CombatKinematicsComponent kinematics,
                ref ProjectileContinuousStepComponent step)
            {
                step.Origin = kinematics.Position;
            }
        }
    }
}
