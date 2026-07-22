using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Aoe Interval Spawn", fileName = "NewAoeIntervalSpawnTrigger")]
    public sealed class AoeIntervalSpawnTrigger : TriggerLink
    {
        [Min(0.01f)] public float energyPerSecond = 2f;
        [FormerlySerializedAs("intervalJitterPercent")]
        [Range(0f, 100f)] public float energyJitterPercent;
        [Min(0)] public int echoCount;
        [Min(0f)] public float scatterRadius;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Aoe;
    }
}
