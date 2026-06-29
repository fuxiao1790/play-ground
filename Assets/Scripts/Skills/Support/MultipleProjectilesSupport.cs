using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple Projectiles", fileName = "MultipleProjectilesSupport")]
    public sealed class MultipleProjectilesSupport : StatModifierSupport, IProjectileBehaviorModifier
    {
        [SerializeField, Min(1)] private int count = 3;
        [SerializeField, Min(0f)] private float spreadDegrees = 30f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public void ApplyToProjectile(ProjectileBehaviorContext ctx)
        {
            ctx.Count = count;
            ctx.SpreadDegrees = spreadDegrees;
        }
    }
}
