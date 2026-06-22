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
        public int TargetMask;
        public int TypeId;
        public float LifetimeSeconds;
        public float TickIntervalSeconds;
        public PlayGround.System.Aoe.AoeSpawnGeometry AoeGeometry;
        public AoeProjectileBurstSnapshot ProjectileBurst;
        public float CritChance;
        public float CritMultiplier;
        // Bounded AOE hit-spawn tail. This caps stacking-skill composition at
        // three skills for now without recursive value-type snapshots.
        public AoeOnHitSpawnSnapshot AoeOnHitSpawn;

        public readonly bool Enabled => Kind != StackDetonationKind.None && TypeId >= 0;
    }

    // Fire-time stack payload carried by applicators. One payload means one owned
    // stack accumulator; composition is handled by ordinary hit-spawn links.
    public struct StackEffectSnapshot
    {
        public int DebuffKey;
        public int Threshold;
        public float Lifetime;
        public StackContribution Contribution;
        public DetonationSnapshot Detonation;

        public readonly bool Enabled =>
            DebuffKey >= 0
            && Threshold > 0
            && Lifetime > 0f
            && Detonation.Enabled;
    }
}
