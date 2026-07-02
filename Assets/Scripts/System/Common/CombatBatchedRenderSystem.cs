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
        private ComponentTypeHandle<CombatRenderElement> renderElementHandle;
        private ComponentTypeHandle<CombatRenderBatchId> renderBatchIdHandle;
        private ComponentTypeHandle<CombatRenderActiveTag> renderActiveHandle;
        private readonly Dictionary<int, NativeList<Matrix4x4>> _batchBuffers = new();

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
        }

        protected override void OnDestroy()
        {
            foreach (NativeList<Matrix4x4> buffer in _batchBuffers.Values)
            {
                if (buffer.IsCreated)
                    buffer.Dispose();
            }
            _batchBuffers.Clear();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            LastActiveProjectileCount = 0;
            LastActiveAoeCount = 0;

            var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
            PrepareBatchBuffers(registry);

            renderElementHandle = GetComponentTypeHandle<CombatRenderElement>(true);
            renderBatchIdHandle = GetComponentTypeHandle<CombatRenderBatchId>(true);
            renderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(true);

            LastActiveProjectileCount = Scatter(projectileRenderQuery);
            LastActiveAoeCount = Scatter(aoeRenderQuery);

            foreach (KeyValuePair<int, CombatRenderResourceEntry> pair in registry.Entries)
            {
                NativeList<Matrix4x4> buffer = _batchBuffers[pair.Key];
                if (buffer.Length > 0)
                    SubmitAll(buffer.AsArray(), pair.Value);
            }
        }

        private void PrepareBatchBuffers(CombatRenderResourceRegistry registry)
        {
            foreach (NativeList<Matrix4x4> buffer in _batchBuffers.Values)
                buffer.Clear();

            foreach (int batchId in registry.Entries.Keys)
            {
                if (!_batchBuffers.ContainsKey(batchId))
                    _batchBuffers.Add(batchId, new NativeList<Matrix4x4>(Allocator.Persistent));
            }
        }

        private int Scatter(EntityQuery query)
        {
            int activeCount = 0;
            using NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            foreach (ArchetypeChunk chunk in chunks)
            {
                NativeArray<CombatRenderElement> elements = chunk.GetNativeArray(ref renderElementHandle);
                NativeArray<CombatRenderBatchId> batchIds = chunk.GetNativeArray(ref renderBatchIdHandle);
                EnabledMask activeMask = chunk.GetEnabledMask(ref renderActiveHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!activeMask[i])
                        continue;

                    int batchId = batchIds[i].Value;
                    if (!_batchBuffers.TryGetValue(batchId, out NativeList<Matrix4x4> buffer))
                        continue;

                    buffer.Add(elements[i].objectToWorld);
                    activeCount++;
                }
            }
            return activeCount;
        }

        private void SubmitAll(NativeArray<Matrix4x4> elements, CombatRenderResourceEntry entry)
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
                Graphics.RenderMeshInstanced(rp, resources.Mesh, 0, elements, count, start);
            }
        }
    }
}
