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

            CombatVfxRoot.Instance?.DrainAndDispatch(buffer.AsNativeArray());
            buffer.Clear();
        }
    }
}
