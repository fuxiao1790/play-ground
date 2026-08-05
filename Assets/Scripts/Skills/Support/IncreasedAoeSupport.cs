using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Aoe Effect", fileName = "IncreasedAoeSupport")]
    public sealed class IncreasedAoeSupport : StatModifierSupport, IAreaSizeModifiers.IIncreasedModifier, IManaModifiers.IIncreasedModifier
    {
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
        private float areaSizeMultiplier = 1.5f;
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent"), Min(0f), Tooltip("Direct mana-cost increase multiplier. 1.1 means 1.1x mana cost.")]
        private float manaCostIncreased = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        void IAreaSizeModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.AreaSize, areaSizeMultiplier);
        void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.ManaCost, manaCostIncreased);
    }
}
