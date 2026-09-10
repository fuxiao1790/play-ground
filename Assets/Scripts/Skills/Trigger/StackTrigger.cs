using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Stack Trigger", fileName = "NewStackTrigger")]
    public sealed class StackTrigger : TriggerLink
    {
        [Min(1)] public int stackThreshold = 3;
        [Min(0f)] public float debuffLifetimeSeconds = 4f;
        [Min(1)] public int stacksPerHit = 1;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Any;
        public override SkillDefinitionTags TargetSkillTags =>
            SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
    }
}
