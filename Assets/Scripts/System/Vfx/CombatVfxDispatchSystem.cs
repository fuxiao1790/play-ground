using Unity.Entities;

namespace PlayGround.System.Vfx
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatVfxDispatchSystem : SystemBase
    {
        private EntityQuery vfxQuery;

        protected override void OnCreate()
        {
            vfxQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<VfxSingleton>());
            if (vfxQuery.IsEmptyIgnoreFilter)
            {
                Entity vfxEntity = EntityManager.CreateEntity(typeof(VfxSingleton));
                EntityManager.AddBuffer<VfxSpawnRequestElement>(vfxEntity);
                return;
            }

            Entity existingVfxEntity = vfxQuery.GetSingletonEntity();
            if (!EntityManager.HasBuffer<VfxSpawnRequestElement>(existingVfxEntity))
            {
                EntityManager.AddBuffer<VfxSpawnRequestElement>(existingVfxEntity);
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            Entity vfxEntity = vfxQuery.GetSingletonEntity();
            DynamicBuffer<VfxSpawnRequestElement> buffer = EntityManager.GetBuffer<VfxSpawnRequestElement>(vfxEntity);
            if (buffer.Length == 0)
            {
                return;
            }

            CombatVfxRoot.Instance?.DrainAndDispatch(buffer.AsNativeArray());
            buffer.Clear();
        }
    }
}
