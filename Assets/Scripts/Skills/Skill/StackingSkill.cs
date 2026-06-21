using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Stacking Skill", fileName = "NewStackingSkill")]
    public sealed class StackingSkill : Skill
    {
        [SerializeField] private StackingSkillDefinition definition = new();

        public override SkillDefinitionTags Tags => definition.SelectedTags;
        public override SkillDefinition Definition => definition;
    }
}
