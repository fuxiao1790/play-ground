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

        private EntityQuery projectileRenderQuery;
        private EntityQuery aoeRenderQuery;
        private NativeArray<CombatRenderElement> submitBuffer;

        protected override void OnCreate()
        {
            Entity registryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(registryEntity, new CombatRenderResourceRegistry());

            projectileRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderElement>()
                .WithAll<CombatRenderBatchId>()
                .WithAll<CombatRenderActiveTag>()
                .WithAll<ProjectileTag>()
                .Build(this);

            aoeRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderElement>()
                .WithAll<CombatRenderBatchId>()
                .WithAll<CombatRenderActiveTag>()
                .WithAll<AoeTag>()
                .Build(this);

            submitBuffer = new NativeArray<CombatRenderElement>(MaxInstancesPerDraw, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (submitBuffer.IsCreated)
            {
                submitBuffer.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
            foreach (KeyValuePair<int, CombatRenderResourceEntry> pair in registry.Entries)
            {
                SubmitBatchId(pair.Key, pair.Value);
            }
        }

        private void SubmitBatchId(int batchId, CombatRenderResourceEntry entry)
        {
            CombatRenderBatchId filter = new CombatRenderBatchId { Value = batchId };

            projectileRenderQuery.SetSharedComponentFilter(filter);
            using NativeArray<CombatRenderElement> projElements =
                projectileRenderQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
            SubmitBatches(projElements, entry.Resources, entry.Layer, entry.BoundsHalfExtent);
            projectileRenderQuery.ResetFilter();

            aoeRenderQuery.SetSharedComponentFilter(filter);
            using NativeArray<CombatRenderElement> aoeElements =
                aoeRenderQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
            SubmitBatches(aoeElements, entry.Resources, entry.Layer, entry.BoundsHalfExtent);
            aoeRenderQuery.ResetFilter();
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
