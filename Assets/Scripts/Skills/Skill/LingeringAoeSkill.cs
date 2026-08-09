using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Lingering AOE Skill", fileName = "NewLingeringAoeSkill")]
    public sealed class LingeringAoeSkill : AoeSkillBase
    {
        [SerializeField] private LingeringAoeDefinition definition = new();

        public override SkillDefinitionTags Tags => SkillDefinitionTags.Aoe | SkillDefinitionTags.Interval;
        public override SkillDefinition Definition => definition;
    }
}
