using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct AoeSimulationSystem : ISystem
    {
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<CombatScope>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (scopeQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            Entity scope = scopeQuery.GetSingletonEntity();
            state.EntityManager.GetBuffer<CombatDamageElement>(scope).Clear();
        }
    }
}
