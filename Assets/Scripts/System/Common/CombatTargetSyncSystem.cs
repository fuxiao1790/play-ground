using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    internal partial struct CombatTargetSyncSystem : ISystem
    {
        private EntityQuery syncQuery;

        void ISystem.OnCreate(ref SystemState state)
        {
            syncQuery = new EntityQueryBuilder(state.WorldUpdateAllocator)
                .WithAll<CombatTargetSyncSource>()
                .WithAllRW<CombatTargetElement>()
                .Build(ref state);
            state.RequireForUpdate(syncQuery);
        }

        void ISystem.OnUpdate(ref SystemState state)
        {
            using NativeArray<Entity> entities = syncQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                CombatTargetSyncSource source = state.EntityManager.GetComponentObject<CombatTargetSyncSource>(entities[i]);
                if (source.Sync == null)
                {
                    continue;
                }
                DynamicBuffer<CombatTargetElement> buffer = state.EntityManager.GetBuffer<CombatTargetElement>(entities[i]);
                source.Sync(buffer);
            }
        }
    }
}
