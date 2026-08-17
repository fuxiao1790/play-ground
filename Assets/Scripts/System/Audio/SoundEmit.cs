using PlayGround.System.Combat.Core;
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
            CombatFaction faction,
            in NativeQueue<SoundEvent>.ParallelWriter sounds)
        {
            if (clipId <= 0)
            {
                return;
            }

            sounds.Enqueue(new SoundEvent
            {
                ClipId = clipId,
                Position = position,
                Velocity = default,
                AudibleRadius = audibleRadius,
                Category = category,
                Priority = faction == CombatFaction.Player ? (short)1 : (short)0
            });
        }
    }
}
