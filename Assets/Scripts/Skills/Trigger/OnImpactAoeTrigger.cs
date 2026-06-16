using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Impact AOE", fileName = "NewOnImpactAoeTrigger")]
    public sealed class OnImpactAoeTrigger : TriggerLink
    {
        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Aoe;
    }
}
