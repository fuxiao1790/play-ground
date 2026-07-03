using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace PlayGround.System.Common
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatBatchedRenderSystem : SystemBase
    {
        private const int InstanceDataStride = 96;
        private const int InitialInstanceCapacity = 1024;
        private static readonly int InstanceDataProperty = Shader.PropertyToID("_InstanceData");

        private EntityQuery projectileRenderQuery;
        private EntityQuery aoeRenderQuery;
        private ComponentTypeHandle<CombatRenderElement> renderElementHandle;
        private ComponentTypeHandle<CombatRenderComponent> renderComponentHandle;
        private ComponentTypeHandle<CombatRenderActiveTag> renderActiveHandle;
        private NativeList<CombatInstanceData> _instances;
        private GraphicsBuffer _instanceBuffer;
        private GraphicsBuffer _argsBuffer;
        private int _instanceCapacity;

        internal int LastActiveProjectileCount;
        internal int LastActiveAoeCount;

        protected override void OnCreate()
        {
            Entity registryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(registryEntity, new CombatRenderResourceRegistry());

            Assert.AreEqual(InstanceDataStride, UnsafeUtility.SizeOf<CombatInstanceData>());
            _instances = new NativeList<CombatInstanceData>(Allocator.Persistent);

            if (!SystemInfo.supportsIndirectArgumentsBuffer)
            {
                Debug.LogError("Combat indirect rendering requires supportsIndirectArgumentsBuffer; CombatBatchedRenderSystem disabled.");
                Enabled = false;
                return;
            }

            _argsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);

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
            CombatIndirectRenderData.Clear();
            if (_instances.IsCreated) _instances.Dispose();
            _instanceBuffer?.Dispose();
            _argsBuffer?.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            LastActiveProjectileCount = 0;
            LastActiveAoeCount = 0;

            var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
            if (registry.SharedMesh == null)
            {
                CombatIndirectRenderData.Clear();
                return;
            }

            _instances.Clear();

            renderElementHandle = GetComponentTypeHandle<CombatRenderElement>(true);
            renderComponentHandle = GetComponentTypeHandle<CombatRenderComponent>(true);
            renderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(true);

            LastActiveProjectileCount = Scatter(projectileRenderQuery);
            LastActiveAoeCount = Scatter(aoeRenderQuery);

            int activeCount = _instances.Length;
            if (activeCount == 0)
            {
                CombatIndirectRenderData.Clear();
                return;
            }

            EnsureInstanceCapacity(activeCount);
            _instanceBuffer.SetData(_instances.AsArray(), 0, 0, activeCount);
            PopulateArgs(registry, activeCount);
            Submit(registry);
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

                    float4 originU = renders[i].UvOriginU;
                    float4 v = renders[i].UvV;
                    _instances.Add(new CombatInstanceData
                    {
                        objectToWorld = elements[i].objectToWorld,
                        uvOriginU = new Vector4(originU.x, originU.y, originU.z, originU.w),
                        uvV = new Vector4(v.x, v.y, v.z, v.w)
                    });
                    activeCount++;
                }
            }
            return activeCount;
        }

        private void EnsureInstanceCapacity(int count)
        {
            if (count <= _instanceCapacity && _instanceBuffer != null) return;

            int newCapacity = math.max(count, _instanceCapacity == 0 ? InitialInstanceCapacity : _instanceCapacity * 2);
            while (newCapacity < count)
                newCapacity *= 2;

            _instanceBuffer?.Dispose();
            _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, InstanceDataStride);
            _instanceCapacity = newCapacity;
        }

        private void PopulateArgs(CombatRenderResourceRegistry registry, int activeCount)
        {
            var args = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = registry.SharedMesh.GetIndexCount(0),
                instanceCount = (uint)activeCount,
                startIndex = registry.SharedMesh.GetIndexStart(0),
                baseVertexIndex = registry.SharedMesh.GetBaseVertex(0),
                startInstance = 0
            };
            _argsBuffer.SetData(new[] { args });
        }

        private void Submit(CombatRenderResourceRegistry registry)
        {
            // A GraphicsBuffer bound via MaterialPropertyBlock does not take effect for indirect
            // draws (known Unity behavior) — it must be set on the material directly. Bind every
            // frame so it survives buffer growth (new buffer identity) and material rebuilds.
            registry.SharedMaterial.SetBuffer(InstanceDataProperty, _instanceBuffer);

            // Do NOT call Graphics.RenderMeshIndirect here — URP's 2D Renderer does not execute
            // immediate-mode render requests. Publish the draw inputs for CombatIndirectRenderFeature
            // to record inside the 2D render pass instead.
            CombatIndirectRenderData.Publish(registry.SharedMesh, registry.SharedMaterial, _argsBuffer);
        }
    }
}
