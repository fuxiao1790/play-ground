using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On AOE Hit Spawn", fileName = "NewOnAoeHitSpawnTrigger")]
    public sealed class OnAoeHitSpawnTrigger : TriggerLink
    {
        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Aoe;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Aoe;
    }
}
