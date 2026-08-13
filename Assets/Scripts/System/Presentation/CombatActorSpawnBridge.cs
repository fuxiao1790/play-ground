using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Targets;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Presentation
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(TargetProxyDeleteApplySystem))]
    public partial class CombatActorSpawnBridge : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetProxySpawnResult>());
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                using NativeArray<TargetProxySpawnResult> results = EntityManager
                    .GetBuffer<TargetProxySpawnResult>(scope)
                    .ToNativeArray(Allocator.Temp);

                for (int resultIndex = 0; resultIndex < results.Length; resultIndex++)
                {
                    TargetProxySpawnResult result = results[resultIndex];
                    if (!CombatTargetProxy.TryTakePendingCreate(result.Token, out ICombatTarget target))
                    {
                        EntityManager.DestroyEntity(result.Proxy);
                        continue;
                    }

                    EntityManager.AddComponentObject(result.Proxy, new TargetCompanion { Target = target });
                    target.CombatTargetProxy = result.Proxy;
                    target.OnCombatSpawned(result.Proxy);
                }

                EntityManager.GetBuffer<TargetProxySpawnResult>(scope).Clear();
            }
        }
    }
}
