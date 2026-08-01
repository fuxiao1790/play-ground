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
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent")]
        private float manaCostIncreased;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        void IRateModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) =>
            sink.Add(SkillStat.Rate, increasedRatePercent * 0.01f);
        void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.ManaCost, manaCostIncreased);
    }
}
