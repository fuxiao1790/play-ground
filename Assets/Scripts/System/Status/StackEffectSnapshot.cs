namespace PlayGround.System.Common
{
    public enum StackDetonationKind
    {
        None = 0,
        Aoe = 1,
        Projectile = 2
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
