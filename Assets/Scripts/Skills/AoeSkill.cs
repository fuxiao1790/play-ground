using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/AOE Skill", fileName = "NewAoeSkill")]
    public sealed class AoeSkill : Skill
    {
        [SerializeField] private AoeDefinition definition = new();

        public override SkillDefinitionTags Tags => SkillDefinitionTags.Aoe;
        public override SkillDefinition Definition => definition;
    }
}
