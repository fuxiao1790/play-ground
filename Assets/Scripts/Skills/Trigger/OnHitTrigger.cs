using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Hit", fileName = "NewOnHitTrigger")]
    public sealed class OnHitTrigger : TriggerLink
    {
        public override SkillDefinitionTags SourceSkillTags =>
            SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;

        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Any;
    }
}
