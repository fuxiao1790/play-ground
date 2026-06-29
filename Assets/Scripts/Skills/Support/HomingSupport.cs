using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Homing", fileName = "HomingSupport")]
    public sealed class HomingSupport : StatModifierSupport, IProjectileBehaviorModifier
    {
        [SerializeField] private float trackingTurnSpeedDegrees = 180f;
        [SerializeField] private float trackingQueryIntervalSeconds = 0.1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public void ApplyToProjectile(ProjectileBehaviorContext ctx)
        {
            ctx.EnableTracking(trackingTurnSpeedDegrees, trackingQueryIntervalSeconds);
        }
    }
}
