using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Targets;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Presentation
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CombatApplyBridge))]
    [UpdateAfter(typeof(CombatActorSpawnBridge))]
    [UpdateBefore(typeof(TargetProxyDeleteApplySystem))]
    public partial class CombatDespawnBridge : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<CombatDespawnEvent>());
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                DynamicBuffer<CombatDespawnEvent> buffer =
                    EntityManager.GetBuffer<CombatDespawnEvent>(scope);
                using NativeArray<CombatDespawnEvent> events = buffer.ToNativeArray(Allocator.Temp);

                for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                {
                    ICombatTarget target = CombatTargetBridge.ResolveTarget(
                        EntityManager,
                        events[eventIndex].Proxy);
                    if (target == null)
                    {
                        continue;
                    }

                    target.CombatTargetProxy = Entity.Null;
                    target.OnCombatDespawned();
                }

                buffer.Clear();
            }
        }
    }
}
