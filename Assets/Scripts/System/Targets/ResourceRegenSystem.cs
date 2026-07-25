using Unity.Entities;
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
            if (deltaTime <= 0f)
            {
                return;
            }

            foreach (RefRW<Health> health in SystemAPI.Query<RefRW<Health>>())
            {
                if (health.ValueRO.Current <= 0f || health.ValueRO.RegenPerSecond <= 0f)
                {
                    continue;
                }

                health.ValueRW.Current = math.min(
                    health.ValueRO.Max,
                    health.ValueRO.Current + health.ValueRO.RegenPerSecond * deltaTime);
            }

            foreach (RefRW<Mana> mana in SystemAPI.Query<RefRW<Mana>>())
            {
                if (mana.ValueRO.RegenPerSecond <= 0f)
                {
                    continue;
                }

                mana.ValueRW.Current = math.clamp(
                    mana.ValueRO.Current + mana.ValueRO.RegenPerSecond * deltaTime,
                    0f,
                    mana.ValueRO.Max);
            }
        }
    }
}
