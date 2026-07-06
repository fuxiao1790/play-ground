using Unity.Burst;
using Unity.Entities;

namespace PlayGround.System.Common
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CombatLifetimeSystem))]
    public partial struct CombatArmingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new CombatArmingJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Active), typeof(ArmingTag))]
        private partial struct CombatArmingJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref CombatArmingComponent arming,
                EnabledRefRW<ArmingTag> armingTag)
            {
                arming.Remaining -= DeltaTime;
                if (arming.Remaining <= 0f)
                {
                    arming.Remaining = 0f;
                    armingTag.ValueRW = false;
                }
            }
        }
    }
}
