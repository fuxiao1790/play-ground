using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Vfx
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatVfxDispatchSystem : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<VfxSpawnRequestElement>());
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            Entity scope = scopeQuery.GetSingletonEntity();
            DynamicBuffer<VfxSpawnRequestElement> buffer = EntityManager.GetBuffer<VfxSpawnRequestElement>(scope);
            if (buffer.Length == 0)
            {
                return;
            }

            using var playerRequests = new NativeList<VfxSpawnRequestElement>(buffer.Length, Allocator.Temp);
            using var mobRequests = new NativeList<VfxSpawnRequestElement>(buffer.Length, Allocator.Temp);
            for (int i = 0; i < buffer.Length; i++)
            {
                VfxSpawnRequestElement e = buffer[i];
                if (e.Faction == CombatFaction.Player)
                {
                    playerRequests.Add(e);
                }
                else if (e.Faction == CombatFaction.Mob)
                {
                    mobRequests.Add(e);
                }
            }
            buffer.Clear();

            if (CombatVfxRoot.TryGetByFaction(CombatFaction.Player, out CombatVfxRoot playerRoot))
            {
                playerRoot.DrainAndDispatch(playerRequests.AsArray());
            }

            if (CombatVfxRoot.TryGetByFaction(CombatFaction.Mob, out CombatVfxRoot mobRoot))
            {
                mobRoot.DrainAndDispatch(mobRequests.AsArray());
            }
        }
    }
}
