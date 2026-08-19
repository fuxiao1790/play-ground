using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Audio
{
    public static class SoundEmit
    {
        public static void Enqueue(
            int clipId,
            float2 position,
            float audibleRadius,
            SoundCategory category,
            in NativeParallelMultiHashMap<int, SoundEvent>.ParallelWriter eventsByClip,
            in NativeParallelHashSet<int>.ParallelWriter clipIds)
        {
            if (clipId <= 0)
            {
                return;
            }

            clipIds.Add(clipId);
            eventsByClip.Add(clipId, EventFor(
                clipId,
                position,
                audibleRadius,
                category));
        }

        public static void Enqueue(
            int clipId,
            float2 position,
            float audibleRadius,
            SoundCategory category,
            NativeParallelMultiHashMap<int, SoundEvent> eventsByClip,
            NativeParallelHashSet<int> clipIds)
        {
            if (clipId <= 0)
            {
                return;
            }

            clipIds.Add(clipId);
            eventsByClip.Add(clipId, EventFor(
                clipId,
                position,
                audibleRadius,
                category));
        }

        private static SoundEvent EventFor(
            int clipId,
            float2 position,
            float audibleRadius,
            SoundCategory category) =>
            new()
            {
                ClipId = clipId,
                Position = position,
                Velocity = default,
                AudibleRadius = audibleRadius,
                Category = category,
                Priority = 0
            };
    }
}
