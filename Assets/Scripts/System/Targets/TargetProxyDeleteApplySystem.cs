using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Core;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CombatApplyBridge))]
    public partial class TargetProxyDeleteApplySystem : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetProxyDeleteEvent>());
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                DynamicBuffer<TargetProxyDeleteEvent> buffer =
                    EntityManager.GetBuffer<TargetProxyDeleteEvent>(scope);
                using NativeArray<TargetProxyDeleteEvent> events = buffer.ToNativeArray(Allocator.Temp);

                for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                {
                    Entity proxy = events[eventIndex].Proxy;
                    if (EntityManager.Exists(proxy))
                    {
                        EntityManager.DestroyEntity(proxy);
                    }
                }

                EntityManager.GetBuffer<TargetProxyDeleteEvent>(scope).Clear();
            }
        }
    }
}
