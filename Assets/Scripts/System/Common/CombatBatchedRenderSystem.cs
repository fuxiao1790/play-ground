using System.Collections.Generic;
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
        private EntityQuery renderQuery;
        private NativeArray<CombatRenderElement> submitBuffer;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScopeRenderCatalog>());

            renderQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatRenderElement>(),
                ComponentType.ReadOnly<CombatRenderScope>(),
                ComponentType.ReadOnly<CombatRenderTypeId>(),
                ComponentType.ReadOnly<CombatRenderActiveTag>());

            submitBuffer = new NativeArray<CombatRenderElement>(MaxInstancesPerDraw, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }

            scopeQuery.Dispose();
            renderQuery.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int s = 0; s < scopes.Length; s++)
            {
                Entity scope = scopes[s];
                CombatScopeRenderCatalog catalog = EntityManager.GetComponentObject<CombatScopeRenderCatalog>(scope);
                if (catalog.Resources.Count == 0)
                {
                    continue;
                }

                foreach (KeyValuePair<int, CombatSpriteRenderResources> pair in catalog.Resources)
                {
                    renderQuery.SetSharedComponentFilter(
                        new CombatRenderScope { Scope = scope },
                        new CombatRenderTypeId { TypeId = pair.Key });

                    using NativeArray<CombatRenderElement> elements =
                        renderQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);

                    SubmitBatches(elements, pair.Value, catalog.Layer, catalog.BoundsHalfExtent);
                    renderQuery.ResetFilter();
                }
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
