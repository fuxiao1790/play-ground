using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Targets
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CombatApplyBridge))]
    public partial class TargetProxyDeleteApplySystem : SystemBase
    {
        private EntityQuery scopeQuery;

        protected override void OnCreate()
        {
            scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetProxyDeleteEvent>());
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            NativeArray<ArchetypeChunk> scopeChunks = scopeQuery.ToArchetypeChunkArray(Allocator.TempJob);
            NativeList<Entity> proxies = new(Allocator.TempJob);
            JobHandle collectHandle = new CollectProxyDeletesJob
            {
                ScopeChunks = scopeChunks,
                EventHandle = GetBufferTypeHandle<TargetProxyDeleteEvent>(false),
                EntityStorage = GetEntityStorageInfoLookup(),
                Proxies = proxies
            }.Schedule(Dependency);
            collectHandle.Complete();

            if (proxies.Length > 0)
            {
                EntityManager.DestroyEntity(proxies.AsArray());
            }

            scopeChunks.Dispose();
            proxies.Dispose();
        }

        [BurstCompile]
        private struct CollectProxyDeletesJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> ScopeChunks;
            public BufferTypeHandle<TargetProxyDeleteEvent> EventHandle;
            [ReadOnly] public EntityStorageInfoLookup EntityStorage;
            public NativeList<Entity> Proxies;

            public void Execute()
            {
                for (int chunkIndex = 0; chunkIndex < ScopeChunks.Length; chunkIndex++)
                {
                    BufferAccessor<TargetProxyDeleteEvent> accessor =
                        ScopeChunks[chunkIndex].GetBufferAccessor(ref EventHandle);
                    for (int bufferIndex = 0; bufferIndex < accessor.Length; bufferIndex++)
                    {
                        DynamicBuffer<TargetProxyDeleteEvent> buffer = accessor[bufferIndex];
                        for (int eventIndex = 0; eventIndex < buffer.Length; eventIndex++)
                        {
                            Entity proxy = buffer[eventIndex].Proxy;
                            if (EntityStorage.Exists(proxy))
                            {
                                AddUnique(proxy);
                            }
                        }

                        buffer.Clear();
                    }
                }
            }

            private void AddUnique(Entity proxy)
            {
                for (int i = 0; i < Proxies.Length; i++)
                {
                    if (Proxies[i] == proxy)
                    {
                        return;
                    }
                }

                Proxies.Add(proxy);
            }
        }
    }
}
