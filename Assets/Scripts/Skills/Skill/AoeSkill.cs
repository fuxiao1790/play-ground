using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class AoeSkillBase : Skill
    {
        public override SkillDefinitionTags Tags => SkillDefinitionTags.Aoe;
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/AOE Skill", fileName = "NewAoeSkill")]
    public sealed class AoeSkill : AoeSkillBase
    {
        [SerializeField] private AoeDefinition definition = new();

        public override SkillDefinition Definition => definition;
    }

}
