using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayGround.System.Combat.Application.CombatApplyFinalizeSingleSystem))]
    public partial struct ResourceRegenSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            JobHandle healthHandle = new HealthRegenJob
            {
                DeltaTime = deltaTime
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new ManaRegenJob
            {
                DeltaTime = deltaTime
            }.ScheduleParallel(healthHandle);
        }

        [BurstCompile]
        private partial struct HealthRegenJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(ref Health health)
            {
                health.Current = math.min(
                    health.Max,
                    health.Current + health.RegenPerSecond * DeltaTime);
            }
        }

        [BurstCompile]
        private partial struct ManaRegenJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(ref Mana mana)
            {
                mana.Current = math.clamp(
                    mana.Current + mana.RegenPerSecond * DeltaTime,
                    0f,
                    mana.Max);
            }
        }
    }
}
