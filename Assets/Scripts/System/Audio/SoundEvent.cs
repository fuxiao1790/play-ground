using Unity.Mathematics;

namespace PlayGround.System.Combat.Audio
{
    public enum SoundCategory : byte
    {
        None = 0,
        Cast,
        Hit,
        Spawn,
        Death,
        Ui
    }

    public struct SoundEvent
    {
        public int ClipId;
        public float2 Position;
        public float2 Velocity;
        public float AudibleRadius;
        public SoundCategory Category;
        public short Priority;
    }

    public struct SkillSoundIds
    {
        public int SpawnId;
    }
}
