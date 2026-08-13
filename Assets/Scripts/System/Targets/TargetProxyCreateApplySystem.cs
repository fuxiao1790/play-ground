using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TargetProxyUpdateApplySystem))]
    [UpdateBefore(typeof(TargetSpatialHashSystem))]
    public partial class TargetProxyCreateApplySystem : SystemBase
    {
        private EntityQuery scopeQuery;
        private NativeList<TargetProxySpawnResult> results;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetProxyCreateEvent>(),
                ComponentType.ReadWrite<TargetProxySpawnResult>());
            results = new NativeList<TargetProxySpawnResult>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (results.IsCreated)
            {
                results.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                results.Clear();
                using NativeArray<TargetProxyCreateEvent> events = EntityManager
                    .GetBuffer<TargetProxyCreateEvent>(scope)
                    .ToNativeArray(Allocator.Temp);

                for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                {
                    TargetProxyCreateEvent createEvent = events[eventIndex];
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
                    if (createEvent.DespawnOnDeath != 0)
                    {
                        EntityManager.AddComponent<DespawnOnDeathTag>(proxy);
                    }

                    results.Add(new TargetProxySpawnResult
                    {
                        Token = createEvent.Token,
                        Proxy = proxy
                    });
                }

                DynamicBuffer<TargetProxySpawnResult> spawnResults =
                    EntityManager.GetBuffer<TargetProxySpawnResult>(scope);
                for (int resultIndex = 0; resultIndex < results.Length; resultIndex++)
                {
                    spawnResults.Add(results[resultIndex]);
                }

                EntityManager.GetBuffer<TargetProxyCreateEvent>(scope).Clear();
            }
        }
    }
}
