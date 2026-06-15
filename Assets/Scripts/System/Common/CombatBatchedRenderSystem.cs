using System.Collections.Generic;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatBatchedRenderSystem : SystemBase
    {
        private const int MaxInstancesPerDraw = 1023;

        private EntityQuery scopeQuery;
        private EntityQuery projectileRenderQuery;
        private EntityQuery aoeRenderQuery;
        private NativeArray<CombatRenderElement> submitBuffer;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScopeRenderCatalog>());

            projectileRenderQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatRenderElement>(),
                ComponentType.ReadOnly<CombatRenderScope>(),
                ComponentType.ReadOnly<CombatRenderTypeId>(),
                ComponentType.ReadOnly<CombatRenderActiveTag>(),
                ComponentType.ReadOnly<ProjectileTag>());

            aoeRenderQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatRenderElement>(),
                ComponentType.ReadOnly<CombatRenderScope>(),
                ComponentType.ReadOnly<CombatRenderTypeId>(),
                ComponentType.ReadOnly<CombatRenderActiveTag>(),
                ComponentType.ReadOnly<AoeTag>());

            submitBuffer = new NativeArray<CombatRenderElement>(MaxInstancesPerDraw, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }

            scopeQuery.Dispose();
            projectileRenderQuery.Dispose();
            aoeRenderQuery.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int s = 0; s < scopes.Length; s++)
            {
                Entity scope = scopes[s];
                CombatScopeRenderCatalog catalog = EntityManager.GetComponentData<CombatScopeRenderCatalog>(scope);
                if (!CombatRoot.TryGetRoot(catalog.RootId, out CombatRoot root))
                {
                    continue;
                }

                SubmitDomain(scope, projectileRenderQuery, root.ProjectileRenderResources, root.RenderLayer, root.BatchBoundsHalfExtent);
                SubmitDomain(scope, aoeRenderQuery, root.AoeRenderResources, root.RenderLayer, root.BatchBoundsHalfExtent);
            }
        }

        private void SubmitDomain(
            Entity scope,
            EntityQuery domainQuery,
            IReadOnlyDictionary<int, CombatSpriteRenderResources> resourcesByType,
            int layer,
            float boundsHalfExtent)
        {
            if (resourcesByType.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in resourcesByType)
            {
                domainQuery.SetSharedComponentFilter(
                    new CombatRenderScope { Scope = scope },
                    new CombatRenderTypeId { TypeId = pair.Key });

                using NativeArray<CombatRenderElement> elements =
                    domainQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);

                SubmitBatches(elements, pair.Value, layer, boundsHalfExtent);
                domainQuery.ResetFilter();
            }
        }

        private void SubmitBatches(
            NativeArray<CombatRenderElement> elements,
            CombatSpriteRenderResources resources,
            int layer,
            float boundsHalfExtent)
        {
            for (int start = 0; start < elements.Length; start += MaxInstancesPerDraw)
            {
                int count = Mathf.Min(MaxInstancesPerDraw, elements.Length - start);
                NativeArray<CombatRenderElement>.Copy(elements, start, submitBuffer, 0, count);
                BatchedSpriteRenderer.SubmitBatch(submitBuffer, 0, count, resources, layer, boundsHalfExtent);
            }
        }
    }
}
