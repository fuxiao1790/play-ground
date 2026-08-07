using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Targeted Interval Spawn", fileName = "NewTargetedIntervalSpawnTrigger")]
    public sealed class TargetedIntervalSpawnTrigger : IntervalSpawnTrigger
    {
        [FormerlySerializedAs("count")]
        [Min(0)] public int echoCount;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Targeted;
    }
}
