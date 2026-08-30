using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TargetProxyUpdateApplySystem))]
    [UpdateBefore(typeof(TargetBroadphaseSystem))]
    public partial class TargetProxyCreateApplySystem : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetProxyCreateEvent>());
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                DynamicBuffer<TargetProxyCreateEvent> buffer =
                    EntityManager.GetBuffer<TargetProxyCreateEvent>(scope);
                using NativeArray<TargetProxyCreateEvent> events = buffer.ToNativeArray(Allocator.Temp);

                for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                {
                    TargetProxyCreateEvent createEvent = events[eventIndex];
                    if (!CombatTargetProxy.TryTakePendingCreate(createEvent.Token, out ICombatTarget target))
                    {
                        continue;
                    }

                    Entity proxy = EntityManager.CreateEntity(CombatTargetProxy.Archetype(EntityManager));
                    EntityManager.SetComponentData(proxy, new TargetFaction { Value = createEvent.Faction });
                    EntityManager.SetComponentData(proxy, createEvent.Position);
                    EntityManager.SetComponentData(proxy, createEvent.Shape);
                    EntityManager.SetComponentData(proxy, new Health
                    {
                        Current = createEvent.CurrentHealth,
                        Max = createEvent.MaxHealth,
                        RegenPerSecond = createEvent.HealthRegenPerSecond
                    });
                    EntityManager.SetComponentData(proxy, new Mana
                    {
                        Current = createEvent.CurrentMana,
                        Max = createEvent.MaxMana,
                        RegenPerSecond = createEvent.ManaRegenPerSecond
                    });
                    EntityManager.AddComponentObject(proxy, new TargetCompanion { Target = target });
                    target.CombatTargetProxy = proxy;
                }

                EntityManager.GetBuffer<TargetProxyCreateEvent>(scope).Clear();
            }
        }
    }
}
