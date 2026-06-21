using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Stack Trigger", fileName = "NewStackTrigger")]
    public sealed class StackTrigger : TriggerLink
    {
        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Any;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Any;
    }
}
