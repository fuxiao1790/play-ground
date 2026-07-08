using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Interval Spawn", fileName = "NewIntervalSpawnTrigger")]
    public sealed class IntervalSpawnTrigger : TriggerLink
    {
        [Min(0.01f)] public float intervalSeconds = 0.5f;
        [Range(0f, 100f)] public float intervalJitterPercent;
        [Min(0)] public int spawnCount;
        [Range(0f, 180f)] public float sideSpreadDegrees = 30f;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
    }
}
