using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Projectile Skill", fileName = "NewProjectileSkill")]
    public sealed class ProjectileSkill : Skill
    {
        [SerializeField] private ProjectileDefinition definition = new();

        public override SkillDefinitionTags Tags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Interval;
        public override SkillDefinition Definition => definition;
    }
}
