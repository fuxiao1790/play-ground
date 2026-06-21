using System;
using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Skills
{
    public enum StackingSkillApplicatorKind
    {
        Projectile,
        Aoe,
        LingeringAoe,
    }

    public enum StackingSkillDetonationKind
    {
        Aoe,
        Projectile,
    }

    [Serializable]
    public sealed class StackingSkillDefinition : SkillDefinition
    {
        public StackingSkillApplicatorKind applicatorKind;
        public ProjectileDefinition projectileApplicator = new();
        public AoeDefinition aoeApplicator = new();
        public LingeringAoeDefinition lingeringAoeApplicator = new();

        public StackingSkillDetonationKind detonationKind;
        public AoeDefinition aoeDetonation = new();
        public ProjectileDefinition projectileDetonation = new();

        [Min(1)] public int stackThreshold = 3;
        [Min(0f)] public float debuffLifetimeSeconds = 4f;
        public string debuffName;
        public DebuffStatus cosmeticDebuffStatus = DebuffStatus.Volatile;

        public SkillDefinitionTags SelectedTags => ApplicatorTags() | DetonationTags();

        public SkillDefinition CreateApplicatorCopy()
        {
            return applicatorKind switch
            {
                StackingSkillApplicatorKind.Projectile => projectileApplicator?.DeepCopy(),
                StackingSkillApplicatorKind.Aoe => aoeApplicator?.DeepCopy(),
                StackingSkillApplicatorKind.LingeringAoe => lingeringAoeApplicator?.DeepCopy(),
                _ => null,
            };
        }

        public SkillDefinition CreateDetonationCopy()
        {
            return detonationKind switch
            {
                StackingSkillDetonationKind.Aoe => aoeDetonation?.DeepCopy(),
                StackingSkillDetonationKind.Projectile => projectileDetonation?.DeepCopy(),
                _ => null,
            };
        }

        private SkillDefinitionTags ApplicatorTags()
        {
            return applicatorKind == StackingSkillApplicatorKind.Projectile
                ? SkillDefinitionTags.Projectile
                : SkillDefinitionTags.Aoe;
        }

        private SkillDefinitionTags DetonationTags()
        {
            return detonationKind == StackingSkillDetonationKind.Projectile
                ? SkillDefinitionTags.Projectile
                : SkillDefinitionTags.Aoe;
        }

        public override SkillDefinition DeepCopy()
        {
            return new StackingSkillDefinition
            {
                applicatorKind = applicatorKind,
                projectileApplicator = (ProjectileDefinition)(projectileApplicator?.DeepCopy() ?? new ProjectileDefinition()),
                aoeApplicator = (AoeDefinition)(aoeApplicator?.DeepCopy() ?? new AoeDefinition()),
                lingeringAoeApplicator = (LingeringAoeDefinition)(lingeringAoeApplicator?.DeepCopy() ?? new LingeringAoeDefinition()),
                detonationKind = detonationKind,
                aoeDetonation = (AoeDefinition)(aoeDetonation?.DeepCopy() ?? new AoeDefinition()),
                projectileDetonation = (ProjectileDefinition)(projectileDetonation?.DeepCopy() ?? new ProjectileDefinition()),
                stackThreshold = stackThreshold,
                debuffLifetimeSeconds = debuffLifetimeSeconds,
                debuffName = debuffName,
                cosmeticDebuffStatus = cosmeticDebuffStatus,
            };
        }
    }
}
