using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class TargetedSkillBase : Skill
    {
        public sealed override SkillDefinitionTags Tags => SkillDefinitionTags.Targeted;
    }

    [CreateAssetMenu(menuName = "PlayGround/Skills/Targeted Skill", fileName = "NewTargetedSkill")]
    public sealed class TargetedSkill : TargetedSkillBase
    {
        [SerializeField] private TargetedDefinition definition = new();
        public override SkillDefinition Definition => definition;
    }
}
