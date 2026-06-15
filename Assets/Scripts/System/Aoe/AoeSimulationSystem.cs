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

            NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < scopes.Length; i++)
            {
                state.EntityManager.GetBuffer<CombatDamageElement>(scopes[i]).Clear();
            }

            scopes.Dispose();
        }
    }
}
