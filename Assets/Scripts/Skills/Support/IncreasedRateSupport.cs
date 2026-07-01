using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Skill Speed", fileName = "IncreasedRateSupport")]
    public sealed class IncreasedRateSupport : StatModifierSupport, IIncreasedModifier
    {
        [SerializeField] private float increasedRatePercent = 0.5f; // 0.5 = +50% rate

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        public void CollectIncreases(IncreasedSink sink)
        {
            sink.Add(SkillStat.Rate, increasedRatePercent);
        }
    }
}
