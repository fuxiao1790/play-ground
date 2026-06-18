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

        public void OnCreate(ref SystemState state)
        {
            projectileQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ProjectileTag>(),
                ComponentType.ReadOnly<ProjectileIdentityComponent>(),
                ComponentType.ReadOnly<Active>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }

        public int ActiveCount(ref SystemState state)
        {
            return projectileQuery.CalculateEntityCount();
        }
    }
}
