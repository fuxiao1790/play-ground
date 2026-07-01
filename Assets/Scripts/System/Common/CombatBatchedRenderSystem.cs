using System.Collections.Generic;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatBatchedRenderSystem : SystemBase
    {
        private const int MaxInstancesPerDraw = 1023;

        private EntityQuery projectileRenderQuery;
        private EntityQuery aoeRenderQuery;
        private NativeArray<CombatRenderElement> submitBuffer;

        internal int LastActiveProjectileCount;
        internal int LastActiveAoeCount;

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

            LastActiveProjectileCount = 0;
            LastActiveAoeCount = 0;

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
            LastActiveProjectileCount += projElements.Length;
            SubmitAll(projElements, entry);
            projectileRenderQuery.ResetFilter();

            aoeRenderQuery.SetSharedComponentFilter(filter);
            using NativeArray<CombatRenderElement> aoeElements =
                aoeRenderQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
            LastActiveAoeCount += aoeElements.Length;
            SubmitAll(aoeElements, entry);
            aoeRenderQuery.ResetFilter();
        }

        private void SubmitAll(NativeArray<CombatRenderElement> elements, CombatRenderResourceEntry entry)
        {
            CombatSpriteRenderResources resources = entry.Resources;
            RenderParams rp = new RenderParams(resources.Material)
            {
                matProps = resources.Properties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = entry.Layer,
                worldBounds = new Bounds(
                    Vector3.zero,
                    new Vector3(entry.BoundsHalfExtent, entry.BoundsHalfExtent, entry.BoundsHalfExtent) * 2f)
            };

            for (int start = 0; start < elements.Length; start += MaxInstancesPerDraw)
            {
                int count = math.min(MaxInstancesPerDraw, elements.Length - start);
                NativeArray<CombatRenderElement>.Copy(elements, start, submitBuffer, 0, count);
                Graphics.RenderMeshInstanced(rp, resources.Mesh, 0, submitBuffer, count, 0);
            }
        }
    }
}
