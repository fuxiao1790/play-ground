using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Vfx
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatVfxDispatchSystem : SystemBase
    {
        private EntityQuery catalogQuery;

        protected override void OnCreate()
        {
            catalogQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScopeVfxCatalog>());
        }

        protected override void OnDestroy()
        {
            catalogQuery.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();
            using NativeArray<Entity> scopes = catalogQuery.ToEntityArray(Allocator.Temp);
            for (int s = 0; s < scopes.Length; s++)
            {
                Entity scope = scopes[s];
                CombatScopeVfxCatalog catalog = EntityManager.GetComponentObject<CombatScopeVfxCatalog>(scope);
                catalog.VfxRoot.DrainAndDispatch(EntityManager.GetBuffer<VfxSpawnRequestElement>(scope));
            }
        }
    }
}
