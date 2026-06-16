using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct ProjectileSimulationSystem : ISystem
    {
        private EntityQuery projectileQuery;
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            projectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<ProjectileActiveTag>());
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<CombatScope>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (scopeQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            Entity scope = scopeQuery.GetSingletonEntity();
            state.EntityManager.GetBuffer<CombatDamageElement>(scope).Clear();
        }

        public int ActiveCount(ref SystemState state)
        {
            return projectileQuery.CalculateEntityCount();
        }
    }
}
