using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Homing", fileName = "HomingSupport")]
    public sealed class HomingSupport : StatModifierSupport, IBaseValueModifier, IProjectileBehaviorModifier
    {
        [SerializeField] private float trackingTurnSpeedDegrees = 180f;
        [SerializeField] private float trackingQueryIntervalSeconds = 0.1f;
        [SerializeField, Min(0f)] private float manaCostAdded = 3f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public void CollectAdded(AddedSink sink)
        {
            sink.Add(SkillStat.ManaCost, manaCostAdded);
        }

        public void ApplyToProjectile(ProjectileBehaviorContext ctx)
        {
            ctx.EnableTracking(trackingTurnSpeedDegrees, trackingQueryIntervalSeconds);
        }
    }
}
