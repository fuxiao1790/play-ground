using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Targeted Interval Spawn", fileName = "NewTargetedIntervalSpawnTrigger")]
    public sealed class TargetedIntervalSpawnTrigger : IntervalSpawnTrigger
    {
        [Min(0)] public int count;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Targeted;
    }
}
