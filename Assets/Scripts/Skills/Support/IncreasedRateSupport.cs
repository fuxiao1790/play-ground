using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Skill Speed", fileName = "IncreasedRateSupport")]
    public sealed class IncreasedRateSupport : StatModifierSupport, IRateModifiers.IIncreasedModifier, IManaModifiers.IIncreasedModifier
    {
        [SerializeField, Min(0f), Tooltip("Authored as percent points. 25 means +25% skill rate.")]
        private float increasedRatePercent = 50f;
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent"), Min(0f), Tooltip("Direct mana-cost increase multiplier. 1.1 means 1.1x mana cost.")]
        private float manaCostIncreased = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        void IRateModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) =>
            sink.AddPercent(SkillStat.Rate, increasedRatePercent * 0.01f);
        void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.ManaCost, manaCostIncreased);
    }
}
