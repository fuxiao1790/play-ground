using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Lingering Targeted Skill", fileName = "NewLingeringTargetedSkill")]
    public sealed class LingeringTargetedSkill : TargetedSkillBase
    {
        [SerializeField] private LingeringTargetedDefinition definition = new();

        public override SkillDefinition Definition => definition;
    }
}
