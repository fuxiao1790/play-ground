using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Audio
{
    // ECS Lifecycle: singleton sound event lane; created and disposed by
    // SoundEventDispatchSystem, written during simulation by spawn apply jobs,
    // drained and cleared by SoundEventDispatchSystem during presentation.
    public struct SoundEventSingleton : IComponentData
    {
        public NativeParallelMultiHashMap<int, SoundEvent> EventsByClip;
        public NativeParallelHashSet<int> ClipIds;
        public JobHandle ProducerHandle;
    }

    public static class SoundEventLane
    {
        // Capacity changes require exclusive access. Apply systems already cross a
        // materialization sync point, so reserve there before their single writer runs.
        public static void Reserve(ref SoundEventSingleton lane, int additionalEvents)
        {
            lane.ProducerHandle.Complete();
            lane.ProducerHandle = default;

            if (additionalEvents <= 0)
            {
                return;
            }

            int required = lane.EventsByClip.Count() + additionalEvents;
            EnsureCapacity(ref lane.EventsByClip, required);
            // Every event could theoretically use a distinct clip id.
            EnsureCapacity(ref lane.ClipIds, required);
        }

        private static void EnsureCapacity(
            ref NativeParallelMultiHashMap<int, SoundEvent> map,
            int required)
        {
            if (map.Capacity < required)
            {
                map.Capacity = NextCapacity(required);
            }
        }

        private static void EnsureCapacity(
            ref NativeParallelHashSet<int> set,
            int required)
        {
            if (set.Capacity < required)
            {
                set.Capacity = NextCapacity(required);
            }
        }

        private static int NextCapacity(int required)
        {
            int capacity = 1;
            while (capacity < required && capacity <= int.MaxValue / 2)
            {
                capacity *= 2;
            }

            return capacity < required ? required : capacity;
        }
    }
}
