using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Expire", fileName = "NewOnExpireTrigger")]
    public sealed class OnExpireTrigger : TriggerLink
    {
        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.None;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.None;
    }
}
