using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Recovery Speed", fileName = "IncreasedRecoverySpeedSupport")]
    public sealed class IncreasedRecoverySpeedSupport : AdditiveSupport
    {
        [SerializeField, Min(0.01f)] private float recoverySpeedMultiplier = 1.5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;
        public override float RecoverySpeedMultiplier => recoverySpeedMultiplier;

        public override void Apply(SkillDefinition def)
        {
        }
    }
}
