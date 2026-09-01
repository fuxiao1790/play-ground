using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using PlayGround.System.Combat.Targeted;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;

namespace PlayGround.System.Combat.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatBatchedRenderSystem : SystemBase
    {
        private const int InstanceDataStride = 32;
        private const int InitialInstanceCapacity = 1024;
        private static readonly int InstanceDataProperty = Shader.PropertyToID("_InstanceData");
        private static readonly int UvBasisProperty = Shader.PropertyToID("_UvBasis");
        private static readonly ProfilerMarker WriteDirectWriteMarker = new("CombatBatchedRenderSystem.WriteDirect.Write");
        private static readonly ProfilerMarker WriteDirectMarker = new("CombatBatchedRenderSystem.WriteDirect");
        private EntityQuery renderQuery;
        private ComponentTypeHandle<CombatRenderComponent> renderComponentHandle;
        private GraphicsBuffer _instanceBuffer;
        private int _instanceCapacity;

        protected override void OnCreate()
        {
            Entity registryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(registryEntity, new CombatRenderResourceRegistry());

            Assert.AreEqual(InstanceDataStride, UnsafeUtility.SizeOf<CombatRenderComponent>());

            renderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderComponent>()
                .WithAll<Active>()
                .WithAny<ProjectileTag, AoeTag, TargetedTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);

            renderComponentHandle = GetComponentTypeHandle<CombatRenderComponent>(true);
        }

        protected override void OnDestroy()
        {
            _instanceBuffer?.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
            if (registry.SharedMesh == null)
            {
                return;
            }

            int entityCount = renderQuery.CalculateEntityCount();
            if (entityCount == 0)
            {
                registry.SetActiveInstanceCount(0);
                return;
            }
            GraphicsBuffer uvBasisBuffer = registry.EnsureUvBasisBuffer();

            EnsureInstanceCapacity(entityCount);
            registry.EnsureMeshCapacity(entityCount);

            // No CPU-side staging copy: CompleteDependency() above already guarantees no
            // outstanding job is writing CombatRenderComponent, so each chunk's component
            // array is a direct read-only view into the chunk's real backing memory. Upload
            // straight from that view to the GPU buffer, one SetData per chunk at its running
            // offset, instead of compacting into an intermediate array first.
            renderComponentHandle.Update(this);
            using (WriteDirectMarker.Auto())
            {
                using NativeArray<ArchetypeChunk> chunks = renderQuery.ToArchetypeChunkArray(Allocator.Temp);
                int offset = 0;
                for (int i = 0; i < chunks.Length; i++)
                {
                    ArchetypeChunk chunk = chunks[i];
                    NativeArray<CombatRenderComponent> components = chunk.GetNativeArray(ref renderComponentHandle);
                    _instanceBuffer.SetData(components, 0, offset, chunk.Count);
                    offset += chunk.Count;
                }
            }

            Submit(registry, uvBasisBuffer);
            registry.SetActiveInstanceCount(entityCount);
        }

        private void EnsureInstanceCapacity(int count)
        {
            if (count <= _instanceCapacity && _instanceBuffer != null) return;

            int newCapacity = math.max(count, _instanceCapacity == 0 ? InitialInstanceCapacity : _instanceCapacity * 2);
            while (newCapacity < count)
                newCapacity *= 2;

            _instanceBuffer?.Dispose();
            // Written via SetData, not LockBufferForWrite/UnlockBufferAfterWrite. A
            // LockBufferForWrite buffer uses a transient/rotating GPU allocation, so a
            // Material.SetBuffer binding goes stale across frames; on idle frames (which
            // still draw a 0-index submesh through the never-culled renderer) the SRV
            // reads as unbound -> "requires a buffer _InstanceData ... none provided".
            // A plain structured buffer keeps a stable allocation the binding survives on.
            _instanceBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                newCapacity,
                InstanceDataStride);

            _instanceCapacity = newCapacity;
        }

        private void Submit(CombatRenderResourceRegistry registry, GraphicsBuffer uvBasisBuffer)
        {
            registry.SharedMaterial.SetBuffer(InstanceDataProperty, _instanceBuffer);
            registry.SharedMaterial.SetBuffer(UvBasisProperty, uvBasisBuffer);
        }
    }
}
