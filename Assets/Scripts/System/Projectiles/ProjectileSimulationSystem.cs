using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Projectiles
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
