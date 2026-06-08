using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class AoeSkillBase : Skill
    {
        public sealed override SkillDefinitionTags Tags => SkillDefinitionTags.Aoe;
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/AOE Skill", fileName = "NewAoeSkill")]
    public sealed class AoeSkill : AoeSkillBase
    {
        [SerializeField] private AoeDefinition definition = new();

        public override SkillDefinition Definition => definition;
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Lingering AOE Skill", fileName = "NewLingeringAoeSkill")]
    public sealed class LingeringAoeSkill : AoeSkillBase
    {
        [SerializeField] private LingeringAoeDefinition definition = new();

        public override SkillDefinition Definition => definition;
    }
}
