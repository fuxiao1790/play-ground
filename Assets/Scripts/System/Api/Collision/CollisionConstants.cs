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
namespace PlayGround.System.Combat.Collision
{
    public static class CollisionConstants
    {
        // Hard-coded per-tick hit cap for AOE collision. 32 is far above any real AOE overlap; the clamp is a
        // safety bound, not a gameplay knob. There is intentionally no per-skill
        // override and no config entity.
        public const int MaxAoeTargetsPerTick = 32;

        // In-chunk capacity for ProjectileContactGateElement. Pierce counts are
        // authored small; a projectile that pierces more distinct targets than
        // this over its lifetime pays a one-time heap growth (accepted).
        public const int MaxProjectileGateCapacity = 16;

        // Max time-of-impact candidates a swept projectile resolves in one frame. Mirrors
        // MaxProjectileGateCapacity: a safety bound on the per-entity stack array, not a
        // gameplay knob. When the cap binds, the nearest candidates are kept.
        public const int MaxSweptHitsPerFrame = 16;

    }
}
