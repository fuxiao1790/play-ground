using PlayGround.Mob;

namespace PlayGround.Skills.Runtime
{
    public enum RuntimeStackingSkillEffectKind
    {
        None,
        Projectile,
        Aoe,
    }

    public sealed class RuntimeStackingSkillDefinition : RuntimeSkillDefinition
    {
        public RuntimeSkillDefinition ApplicatorDefinition { get; set; }
        public RuntimeSkillDefinition DetonationDefinition { get; set; }
        public RuntimeStackingSkillEffectKind ApplicatorKind { get; set; }
        public RuntimeStackingSkillEffectKind DetonationKind { get; set; }
        public int StackThreshold { get; set; } = 1;
        public float DebuffLifetimeSeconds { get; set; }
        public string DebuffName { get; set; }
        public DebuffStatus CosmeticDebuffStatus { get; set; }

        // Debuff key is a dedicated registration-minted id. It is not authored,
        // and it stays separate from detonation TypeId so render/type dedup
        // cannot reintroduce stack-state collisions.
        public int DebuffKey { get; set; } = -1;
    }
}
