using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Impact Targeted", fileName = "NewOnImpactTargetedTrigger")]
    public sealed class OnImpactTargetedTrigger : TriggerLink
    {
        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Targeted;
    }
}
