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
namespace PlayGround.System.Combat.Status
{
    public enum StackDetonationKind
    {
        None = 0,
        ImpactAoe = 1,
        LingeringAoe = 2,
        Projectile = 3
    }

    public struct StackContribution
    {
        public float Damage;
        public int ProjectileCount;
        public float AreaSize;
    }

    public struct DetonationSnapshot
    {
        public StackDetonationKind Kind;
        public CombatFaction Faction;
        public Unity.Entities.Hash128 TemplateKey;

        public readonly bool Enabled =>
            Kind != StackDetonationKind.None
            && !TemplateKey.Equals(default(Unity.Entities.Hash128));
    }

    // Fire-time stack payload carried by applicators. One payload means one owned
    // stack accumulator; detonation spawn data is resolved through the template registry.
    public struct StackEffectSnapshot
    {
        public int DebuffKey;
        public int Threshold;
        public int StacksPerHit;
        public float Lifetime;
        public StackContribution Contribution;
        public CombatFaction Faction;
        public StackDetonationKind DetonationKind;
        public Unity.Entities.Hash128 DetonationKey;

        public readonly bool Enabled =>
            DebuffKey >= 0
            && Threshold > 0
            && Lifetime > 0f
            && DetonationKind != StackDetonationKind.None
            && !DetonationKey.Equals(default(Unity.Entities.Hash128));
    }
}
