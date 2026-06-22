namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeStackingDetonation : RuntimeSkillDefinition
    {
        public RuntimeSkillDefinition Detonation { get; set; }
        public int StackThreshold { get; set; } = 1;
        public float DebuffLifetimeSeconds { get; set; }
        public int StacksPerHit { get; set; } = 1;

        // Display/debug label derived by the compiler from the stacking skill asset.
        // Accrual identity is DebuffKey, not this string.
        public string DebuffName { get; set; }

        // Debuff key is a dedicated registration-minted id. It is not authored,
        // and it stays separate from detonation TypeId so render/type dedup
        // cannot reintroduce stack-state collisions.
        public int DebuffKey { get; set; } = -1;
    }
}
