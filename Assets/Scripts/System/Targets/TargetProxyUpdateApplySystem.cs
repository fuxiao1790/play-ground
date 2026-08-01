using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TargetSpatialHashSystem))]
    public partial class TargetProxyUpdateApplySystem : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetProxyUpdateEvent>());
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                DynamicBuffer<TargetProxyUpdateEvent> buffer =
                    EntityManager.GetBuffer<TargetProxyUpdateEvent>(scope);

                for (int eventIndex = 0; eventIndex < buffer.Length; eventIndex++)
                {
                    TargetProxyUpdateEvent updateEvent = buffer[eventIndex];
                    if (!EntityManager.Exists(updateEvent.Proxy))
                    {
                        continue;
                    }

                    switch (updateEvent.Kind)
                    {
                        case TargetProxyUpdateKind.Push:
                            EntityManager.SetComponentData(updateEvent.Proxy, updateEvent.Position);
                            EntityManager.SetComponentData(updateEvent.Proxy, updateEvent.Shape);
                            break;

                        case TargetProxyUpdateKind.PushResourceMaxes:
                            Health health = EntityManager.GetComponentData<Health>(updateEvent.Proxy);
                            health.Max = updateEvent.MaxHealth;
                            health.RegenPerSecond = updateEvent.HealthRegenPerSecond;
                            health.Current = math.clamp(health.Current, 0f, health.Max);
                            EntityManager.SetComponentData(updateEvent.Proxy, health);

                            Mana mana = EntityManager.GetComponentData<Mana>(updateEvent.Proxy);
                            mana.Max = updateEvent.MaxMana;
                            mana.RegenPerSecond = updateEvent.ManaRegenPerSecond;
                            mana.Current = math.clamp(mana.Current, 0f, mana.Max);
                            EntityManager.SetComponentData(updateEvent.Proxy, mana);
                            break;

                        case TargetProxyUpdateKind.SetHealth:
                            Health currentHealth = EntityManager.GetComponentData<Health>(updateEvent.Proxy);
                            currentHealth.Current = math.clamp(updateEvent.CurrentValue, 0f, currentHealth.Max);
                            EntityManager.SetComponentData(updateEvent.Proxy, currentHealth);
                            break;

                        case TargetProxyUpdateKind.SetMana:
                            Mana currentMana = EntityManager.GetComponentData<Mana>(updateEvent.Proxy);
                            currentMana.Current = math.clamp(updateEvent.CurrentValue, 0f, currentMana.Max);
                            EntityManager.SetComponentData(updateEvent.Proxy, currentMana);
                            break;
                    }
                }

                buffer.Clear();
            }
        }
    }
}
