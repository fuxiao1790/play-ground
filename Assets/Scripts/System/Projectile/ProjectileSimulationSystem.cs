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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            projectileQuery = state.GetEntityQuery(ComponentType.ReadOnly<ProjectileComponent>());
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<ProjectileScope>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (scopeQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < scopes.Length; i++)
            {
                state.EntityManager.GetBuffer<ProjectileHitElement>(scopes[i]).Clear();
                state.EntityManager.GetBuffer<ProjectileChildSpawnRequestElement>(scopes[i]).Clear();
            }

            scopes.Dispose();
        }

        public int ActiveCount(ref SystemState state)
        {
            return projectileQuery.CalculateEntityCount();
        }
    }
}
