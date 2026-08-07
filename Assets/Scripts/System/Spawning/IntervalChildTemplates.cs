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
namespace PlayGround.System.Combat.Spawning
{
    public enum IntervalChildKind
    {
        Projectile = 0,
        ImpactAoe = 1,
        LingeringAoe = 2,
        Targeted = 3,
        LingeringTargeted = 4
    }

    public struct OnHitSpawnRef
    {
        public IntervalChildKind Kind;
        public Unity.Entities.Hash128 TemplateKey;

        public readonly bool Enabled => !TemplateKey.Equals(default(Unity.Entities.Hash128));
    }
}
