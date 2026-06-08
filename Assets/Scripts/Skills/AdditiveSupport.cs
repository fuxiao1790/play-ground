using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class AdditiveSupport : ScriptableObject
    {
        public abstract SkillDefinitionTags SupportedSkillTags { get; }
        public abstract void Apply(SkillDefinition def);
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple Projectiles", fileName = "MultipleProjectilesSupport")]
    public sealed class MultipleProjectilesSupport : AdditiveSupport
    {
        [SerializeField, Min(1)] private int count = 3;
        [SerializeField, Min(0f)] private float spreadDegrees = 30f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
            {
                p.count = count;
                p.spreadDegrees = spreadDegrees;
            }
        }
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Piercing", fileName = "PiercingSupport")]
    public sealed class PiercingSupport : AdditiveSupport
    {
        [SerializeField, Min(0)] private int pierceCount = 2;
        [SerializeField, Min(0f)] private float repeatHitCooldown = 0.5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
            {
                p.pierceCount = pierceCount;
                p.repeatHitCooldown = repeatHitCooldown;
            }
        }
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Homing", fileName = "HomingSupport")]
    public sealed class HomingSupport : AdditiveSupport
    {
        [SerializeField] private float trackingRange = 20f;
        [SerializeField] private float trackingTurnSpeedDegrees = 180f;
        [SerializeField] private float trackingQueryIntervalSeconds = 0.1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
            {
                p.trackingEnabled = true;
                p.trackingRange = trackingRange;
                p.trackingTurnSpeedDegrees = trackingTurnSpeedDegrees;
                p.trackingQueryIntervalSeconds = trackingQueryIntervalSeconds;
            }
        }
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Faster Projectiles", fileName = "FasterProjectilesSupport")]
    public sealed class FasterProjectilesSupport : AdditiveSupport
    {
        [SerializeField, Min(0.01f)] private float speedMultiplier = 1.5f;
        [SerializeField, Min(0.01f)] private float lifetimeMultiplier = 1.2f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
            {
                p.speed *= speedMultiplier;
                p.lifetime *= lifetimeMultiplier;
            }
        }
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Added Damage", fileName = "AddedDamageSupport")]
    public sealed class AddedDamageSupport : AdditiveSupport
    {
        [SerializeField] private float addedDamage = 5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
                p.damage += addedDamage;
            else if (def is AoeDefinition a)
                a.damage += addedDamage;
        }
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Concentrated Effect", fileName = "ConcentratedEffectSupport")]
    public sealed class ConcentratedEffectSupport : AdditiveSupport
    {
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1.5f;
        [SerializeField] private float addedDamage = 5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public override void Apply(SkillDefinition def)
        {
            if (def is AoeDefinition a)
            {
                a.sizeMultiplier *= sizeMultiplier;
                a.damage += addedDamage;
            }
        }
    }
}
