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
            projectileRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderElement>()
                .WithAll<CombatRenderFaction>()
                .WithAll<CombatRenderTypeId>()
                .WithAll<CombatRenderActiveTag>()
                .WithAll<ProjectileTag>()
                .Build(this);

            aoeRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderElement>()
                .WithAll<CombatRenderFaction>()
                .WithAll<CombatRenderTypeId>()
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

            if (CombatRoot.TryGetByFaction(CombatFaction.Player, out CombatRoot playerRoot))
            {
                SubmitFaction(playerRoot);
            }

            if (CombatRoot.TryGetByFaction(CombatFaction.Mob, out CombatRoot mobRoot))
            {
                SubmitFaction(mobRoot);
            }
        }

        private void SubmitFaction(CombatRoot root)
        {
            SubmitDomain(root.Faction, projectileRenderQuery, root.ProjectileRenderResources, root.RenderLayer, root.BatchBoundsHalfExtent);
            SubmitDomain(root.Faction, aoeRenderQuery, root.AoeRenderResources, root.RenderLayer, root.BatchBoundsHalfExtent);
        }

        private void SubmitDomain(
            CombatFaction faction,
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
                    new CombatRenderFaction { Faction = faction },
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
