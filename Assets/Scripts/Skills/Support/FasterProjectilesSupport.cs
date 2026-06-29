using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Faster Projectiles", fileName = "FasterProjectilesSupport")]
    public sealed class FasterProjectilesSupport : StatModifierSupport, IMultiplierModifier
    {
        [SerializeField, Min(0.01f)] private float speedMultiplier = 1.5f;
        [SerializeField, Min(0.01f)] private float lifetimeMultiplier = 1.2f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public void CollectMultipliers(MultiplierSink sink)
        {
            sink.Add(SkillStat.ProjectileSpeed, speedMultiplier);
            sink.Add(SkillStat.ProjectileLifetime, lifetimeMultiplier);
        }
    }
}
