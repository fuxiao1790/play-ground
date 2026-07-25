using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Projectile Interval Spawn", fileName = "NewProjectileIntervalSpawnTrigger")]
    public sealed class ProjectileIntervalSpawnTrigger : IntervalSpawnTrigger
    {
        [Min(0)] public int projectileCount;
        [Range(0f, 180f)] public float sideSpreadDegrees = 30f;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Projectile;
    }
}
