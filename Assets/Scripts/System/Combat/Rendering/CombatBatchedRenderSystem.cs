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
using Unity.Burst;
using Unity.Burst.Intrinsics;
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
        private NativeList<CombatRenderComponent> _instanceData;

        protected override void OnCreate()
        {
            Entity registryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(registryEntity, new CombatRenderResourceRegistry());

            Assert.AreEqual(InstanceDataStride, UnsafeUtility.SizeOf<CombatRenderComponent>());

            _instanceData = new NativeList<CombatRenderComponent>(Allocator.Persistent);

            renderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderComponent>()
                .WithAll<Active>()
                .WithAny<ProjectileTag, AoeTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);
        }

        protected override void OnDestroy()
        {
            if (_instanceData.IsCreated) _instanceData.Dispose();
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

            _instanceData.Clear();
            EnsureInstanceCapacity(entityCount);
            registry.EnsureMeshCapacity(entityCount);

            renderComponentHandle = GetComponentTypeHandle<CombatRenderComponent>(true);
            using (WriteDirectMarker.Auto())
            {
                new WriteDirectJob
                {
                    InstanceData = _instanceData,
                    ComponentHandle = renderComponentHandle
                }.Run(renderQuery);
            }

            _instanceBuffer.SetData(_instanceData.AsArray(), 0, 0, _instanceData.Length);

            Submit(registry, uvBasisBuffer);
            registry.SetActiveInstanceCount(entityCount);
        }

        [BurstCompile]
        private struct WriteDirectJob : IJobChunk
        {
            public NativeList<CombatRenderComponent> InstanceData;
            [ReadOnly] public ComponentTypeHandle<CombatRenderComponent> ComponentHandle;

            public void Execute(in ArchetypeChunk chunk, int _, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<CombatRenderComponent> components = chunk.GetNativeArray(ref ComponentHandle);
                InstanceData.AddRange(components);
            }
        }

        private void EnsureInstanceCapacity(int count)
        {
            if (count <= _instanceCapacity && _instanceBuffer != null) return;

            int newCapacity = math.max(count, _instanceCapacity == 0 ? InitialInstanceCapacity : _instanceCapacity * 2);
            while (newCapacity < count)
                newCapacity *= 2;

            _instanceBuffer?.Dispose(); 
            _instanceBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 
                GraphicsBuffer.UsageFlags.LockBufferForWrite,
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
