using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Skill Speed", fileName = "IncreasedRateSupport")]
    public sealed class IncreasedRateSupport : StatModifierSupport, IRateModifiers.IIncreasedModifier, IManaModifiers.IIncreasedModifier
    {
        [SerializeField, Min(0f), Tooltip("Authored as percent points. 25 means +25% skill rate.")]
        private float increasedRatePercent = 50f;
        [SerializeField] private float manaCostIncreasedPercent;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        void IRateModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) =>
            sink.Add(SkillStat.Rate, increasedRatePercent * 0.01f);
        void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.ManaCost, manaCostIncreasedPercent);
    }
}
