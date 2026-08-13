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
            foreach (RefRW<Health> health in SystemAPI.Query<RefRW<Health>>())
            {
                float alive = math.select(0f, 1f, health.ValueRO.Current > 0f);
                health.ValueRW.Current = math.min(
                    health.ValueRO.Max,
                    health.ValueRO.Current + health.ValueRO.RegenPerSecond * deltaTime * alive);
            }

            foreach (RefRW<Mana> mana in SystemAPI.Query<RefRW<Mana>>())
            {
                mana.ValueRW.Current = math.clamp(
                    mana.ValueRO.Current + mana.ValueRO.RegenPerSecond * deltaTime,
                    0f,
                    mana.ValueRO.Max);
            }
        }
    }
}
