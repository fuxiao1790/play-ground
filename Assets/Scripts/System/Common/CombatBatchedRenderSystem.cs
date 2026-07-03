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
        private ComponentTypeHandle<CombatRenderComponent> renderComponentHandle;
        private ComponentTypeHandle<CombatRenderActiveTag> renderActiveHandle;
        private NativeList<Matrix4x4> _transforms;
        private NativeList<Vector4> _uvRects;
        private Vector4[] _uvRectScratch;
        private MaterialPropertyBlock _matProps;

        internal int LastActiveProjectileCount;
        internal int LastActiveAoeCount;

        protected override void OnCreate()
        {
            Entity registryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(registryEntity, new CombatRenderResourceRegistry());

            _transforms = new NativeList<Matrix4x4>(Allocator.Persistent);
            _uvRects = new NativeList<Vector4>(Allocator.Persistent);
            _uvRectScratch = new Vector4[MaxInstancesPerDraw];
            _matProps = new MaterialPropertyBlock();

            projectileRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderElement>()
                .WithAll<CombatRenderComponent>()
                .WithAll<CombatRenderActiveTag>()
                .WithAll<ProjectileTag>()
                .Build(this);

            aoeRenderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderElement>()
                .WithAll<CombatRenderComponent>()
                .WithAll<CombatRenderActiveTag>()
                .WithAll<AoeTag>()
                .Build(this);
        }

        protected override void OnDestroy()
        {
            if (_transforms.IsCreated) _transforms.Dispose();
            if (_uvRects.IsCreated) _uvRects.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            LastActiveProjectileCount = 0;
            LastActiveAoeCount = 0;

            var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
            if (registry.SharedMesh == null) return;

            _transforms.Clear();
            _uvRects.Clear();

            renderElementHandle = GetComponentTypeHandle<CombatRenderElement>(true);
            renderComponentHandle = GetComponentTypeHandle<CombatRenderComponent>(true);
            renderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(true);

            LastActiveProjectileCount = Scatter(projectileRenderQuery);
            LastActiveAoeCount = Scatter(aoeRenderQuery);

            if (_transforms.Length > 0)
                SubmitAll(registry);
        }

        // UV rects are stored directly on CombatRenderComponent (computed once when the spawn
        // command was built), not looked up from the registry here — this pass only reads
        // already-prepared per-entity data.
        private int Scatter(EntityQuery query)
        {
            int activeCount = 0;
            using NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            foreach (ArchetypeChunk chunk in chunks)
            {
                NativeArray<CombatRenderElement> elements = chunk.GetNativeArray(ref renderElementHandle);
                NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref renderComponentHandle);
                EnabledMask activeMask = chunk.GetEnabledMask(ref renderActiveHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!activeMask[i] || renders[i].IsRenderable == 0)
                        continue;

                    float4 uvRect = renders[i].UvRect;
                    _transforms.Add(elements[i].objectToWorld);
                    _uvRects.Add(new Vector4(uvRect.x, uvRect.y, uvRect.z, uvRect.w));
                    activeCount++;
                }
            }
            return activeCount;
        }

        private void SubmitAll(CombatRenderResourceRegistry registry)
        {
            RenderParams rp = new RenderParams(registry.SharedMaterial)
            {
                matProps = _matProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = registry.Layer,
                worldBounds = new Bounds(
                    Vector3.zero,
                    new Vector3(
                        CombatRenderResourceRegistry.BoundsHalfExtent,
                        CombatRenderResourceRegistry.BoundsHalfExtent,
                        CombatRenderResourceRegistry.BoundsHalfExtent) * 2f)
            };

            NativeArray<Matrix4x4> transforms = _transforms.AsArray();
            NativeArray<Vector4> uvRects = _uvRects.AsArray();

            for (int start = 0; start < transforms.Length; start += MaxInstancesPerDraw)
            {
                int count = math.min(MaxInstancesPerDraw, transforms.Length - start);
                for (int i = 0; i < count; i++)
                    _uvRectScratch[i] = uvRects[start + i];

                _matProps.SetVectorArray("_UvRect", _uvRectScratch);
                Graphics.RenderMeshInstanced(rp, registry.SharedMesh, 0, transforms, count, start);
            }
        }
    }
}
