using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
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
        private static readonly ProfilerMarker WriteDirectWriteMarker = new("CombatBatchedRenderSystem.WriteDirect.Write");
        private static readonly ProfilerMarker WriteDirectMarker = new("CombatBatchedRenderSystem.WriteDirect");
        private EntityQuery renderQuery;
        private GraphicsBuffer _instanceBuffer;
        private GraphicsBuffer _argsBuffer;
        private int _instanceCapacity;

        protected override void OnCreate()
        {
            Entity registryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(registryEntity, new CombatRenderResourceRegistry());

            Assert.AreEqual(InstanceDataStride, UnsafeUtility.SizeOf<CombatRenderComponent>());

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

            renderQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CombatRenderComponent>()
                .WithAll<CombatRenderActiveTag>()
                .WithAny<ProjectileTag, AoeTag>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(this);
        }

        protected override void OnDestroy()
        {
            CombatIndirectRenderData.Clear();
            _instanceBuffer?.Dispose();
            _argsBuffer?.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
            if (registry.SharedMesh == null)
            {
                CombatIndirectRenderData.Clear();
                return;
            }

            int entityCount = renderQuery.CalculateEntityCount();
            if (entityCount == 0)
            {
                CombatIndirectRenderData.Clear();
                return;
            }

            EnsureInstanceCapacity(entityCount);

            NativeArray<CombatRenderComponent> target = _instanceBuffer.LockBufferForWrite<CombatRenderComponent>(0, entityCount);
            using (WriteDirectMarker.Auto())
            {
                WriteDirect(target);
            }
            _instanceBuffer.UnlockBufferAfterWrite<CombatRenderComponent>(entityCount);

            PopulateArgs(registry, entityCount);
            Submit(registry);
        }

        private void WriteDirect(NativeArray<CombatRenderComponent> target)
        {
            int writeIndex = 0;
            using (WriteDirectWriteMarker.Auto())
            {
                foreach (var component in SystemAPI.Query<RefRO<CombatRenderComponent>>()
                    .WithAll<CombatRenderActiveTag>()
                    .WithAny<ProjectileTag, AoeTag>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
                {
                    target[writeIndex++] = component.ValueRO;
                }
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
            registry.SharedMaterial.SetBuffer(InstanceDataProperty, _instanceBuffer);
            CombatIndirectRenderData.Publish(registry.SharedMesh, registry.SharedMaterial, _argsBuffer);
        }
    }
}
