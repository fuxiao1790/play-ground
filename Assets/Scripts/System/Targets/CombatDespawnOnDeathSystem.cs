using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Core;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatApplyFinalizeSingleSystem))]
    [UpdateBefore(typeof(ResourceRegenSystem))]
    public partial class CombatDespawnOnDeathSystem : SystemBase
    {
        private EntityQuery scopeQuery;
        private NativeList<Entity> pending;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<CombatDespawnEvent>(),
                ComponentType.ReadWrite<TargetProxyDeleteEvent>());
            pending = new NativeList<Entity>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (pending.IsCreated)
            {
                pending.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            pending.Clear();

            foreach ((RefRO<Health> health, Entity proxy) in SystemAPI
                         .Query<RefRO<Health>>()
                         .WithAll<DespawnOnDeathTag>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.Current > 0f)
                {
                    continue;
                }

                pending.Add(proxy);
            }

            // No dedupe state: TargetProxyDeleteApplySystem stays in Presentation this frame.
            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                DynamicBuffer<CombatDespawnEvent> despawnBuffer =
                    EntityManager.GetBuffer<CombatDespawnEvent>(scope);
                DynamicBuffer<TargetProxyDeleteEvent> deleteBuffer =
                    EntityManager.GetBuffer<TargetProxyDeleteEvent>(scope);

                for (int pendingIndex = 0; pendingIndex < pending.Length; pendingIndex++)
                {
                    Entity proxy = pending[pendingIndex];
                    despawnBuffer.Add(new CombatDespawnEvent { Proxy = proxy });
                    deleteBuffer.Add(new TargetProxyDeleteEvent { Proxy = proxy });
                }
            }
        }
    }
}
