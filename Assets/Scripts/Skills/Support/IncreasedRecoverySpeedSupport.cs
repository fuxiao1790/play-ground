using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Recovery Speed", fileName = "IncreasedRecoverySpeedSupport")]
    public sealed class IncreasedRecoverySpeedSupport : StatModifierSupport, IIncreasedModifier
    {
        [SerializeField, Min(0.01f)] private float recoverySpeedMultiplier = 1.5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        public void CollectIncreases(IncreasedSink sink)
        {
            sink.Add(SkillStat.RecoverySpeed, recoverySpeedMultiplier - 1f);
        }
    }
}
