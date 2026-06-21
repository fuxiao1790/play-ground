namespace PlayGround.System.Common
{
    public static class CollisionConstants
    {
        // Hard-coded per-tick hit cap for AOE collision. Also the in-chunk
        // capacity of AoeContactGateElement, so AOE gate growth never allocates
        // within a tick. 32 is far above any real AOE overlap; the clamp is a
        // safety bound, not a gameplay knob. There is intentionally no per-skill
        // override and no config entity.
        public const int MaxAoeTargetsPerTick = 32;

        // In-chunk capacity for ProjectileContactGateElement. Pierce counts are
        // authored small; a projectile that pierces more distinct targets than
        // this over its lifetime pays a one-time heap growth (accepted).
        public const int MaxProjectileGateCapacity = 16;

    }
}
