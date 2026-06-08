using UnityEngine;

namespace PlayGround.Skills
{
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
}
