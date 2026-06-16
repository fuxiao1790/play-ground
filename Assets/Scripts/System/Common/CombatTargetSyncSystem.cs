using Unity.Entities;

namespace PlayGround.System.Common
{
    // The shared CombatTargetElement buffer holds both factions' targets, so
    // it is cleared once here, then each registered CombatRoot appends only
    // its own Faction-tagged entries (CombatRoot.AppendTargetsToBuffer).
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    internal partial struct CombatTargetSyncSystem : ISystem
    {
        private EntityQuery scopeQuery;

        void ISystem.OnCreate(ref SystemState state)
        {
            scopeQuery = new EntityQueryBuilder(state.WorldUpdateAllocator)
                .WithAllRW<CombatTargetElement>()
                .Build(ref state);
            state.RequireForUpdate(scopeQuery);
        }

        void ISystem.OnUpdate(ref SystemState state)
        {
            Entity scope = scopeQuery.GetSingletonEntity();
            DynamicBuffer<CombatTargetElement> buffer = state.EntityManager.GetBuffer<CombatTargetElement>(scope);
            buffer.Clear();

            if (CombatRoot.TryGetByFaction(CombatFaction.Player, out CombatRoot playerRoot))
            {
                playerRoot.AppendTargetsToBuffer(buffer);
            }

            if (CombatRoot.TryGetByFaction(CombatFaction.Mob, out CombatRoot mobRoot))
            {
                mobRoot.AppendTargetsToBuffer(buffer);
            }
        }
    }
}
