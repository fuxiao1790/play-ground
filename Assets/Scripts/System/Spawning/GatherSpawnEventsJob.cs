using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Targeted;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

[assembly: RegisterGenericJobType(typeof(PlayGround.System.Combat.Spawning.GatherSpawnEventsJob<ProjectileSpawnEvent>))]
[assembly: RegisterGenericJobType(typeof(PlayGround.System.Combat.Spawning.GatherSpawnEventsJob<ImpactAoeSpawnEvent>))]
[assembly: RegisterGenericJobType(typeof(PlayGround.System.Combat.Spawning.GatherSpawnEventsJob<LingeringAoeSpawnEvent>))]
[assembly: RegisterGenericJobType(typeof(PlayGround.System.Combat.Spawning.GatherSpawnEventsJob<TargetedSpawnEvent>))]

namespace PlayGround.System.Combat.Spawning
{
    [BurstCompile]
    internal struct GatherSpawnEventsJob<T> : IJob
        where T : unmanaged, IBufferElementData
    {
        public NativeQueue<T> Queue;
        public BufferTypeHandle<T> BufferHandle;
        [ReadOnly] public NativeArray<ArchetypeChunk> ScopeChunks;
        public NativeList<T> Events;

        public void Execute()
        {
            Events.Clear();

            NativeArray<T> queued = Queue.ToArray(Allocator.Temp);
            Events.AddRange(queued);
            queued.Dispose();
            Queue.Clear();

            for (int c = 0; c < ScopeChunks.Length; c++)
            {
                BufferAccessor<T> accessor = ScopeChunks[c].GetBufferAccessor(ref BufferHandle);
                for (int i = 0; i < accessor.Length; i++)
                {
                    DynamicBuffer<T> buffer = accessor[i];
                    Events.AddRange(buffer.AsNativeArray());
                    buffer.Clear();
                }
            }
        }
    }
}
