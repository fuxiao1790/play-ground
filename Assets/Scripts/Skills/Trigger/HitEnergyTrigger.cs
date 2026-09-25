using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Hit Energy Trigger", fileName = "NewHitEnergyTrigger")]
    public sealed class HitEnergyTrigger : TriggerLink
    {
        [SerializeField, Min(1e-3f)] private float energyContributionMultiplier = 1f;
        [SerializeField, Min(1e-3f)] private float energyRequirementMultiplier = 1f;
        [SerializeField, Min(0f)] private float retentionSeconds;

        public float EnergyContributionMultiplier => energyContributionMultiplier;
        public float EnergyRequirementMultiplier => energyRequirementMultiplier;
        public float RetentionSeconds => retentionSeconds;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Any;
        public override SkillDefinitionTags TargetSkillTags =>
            SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
    }
}
