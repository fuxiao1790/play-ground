using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Aoe Effect", fileName = "IncreasedAoeSupport")]
    public sealed class IncreasedAoeSupport : StatModifierSupport,
        IAreaSizeModifiers.IBaseValueModifier,
        IAreaSizeModifiers.IIncreasedModifier,
        IAreaSizeModifiers.IMultiplierModifier,
        IManaModifiers.IBaseValueModifier,
        IManaModifiers.IIncreasedModifier,
        IManaModifiers.IMultiplierModifier
    {
        [SerializeField, Min(0f)]
        private float areaSizeAdded;
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), FormerlySerializedAs("areaSizeMultiplier"), Min(0.01f), Tooltip("Direct area-size increase multiplier. 1.5 means 1.5x area size.")]
        private float areaSizeIncreased = 1.5f;
        [SerializeField, Min(0.01f)]
        private float areaSizeMultiplier = 1f;
        [SerializeField, Min(0f)]
        private float manaCostAdded;
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent"), Min(0f), Tooltip("Direct mana-cost increase multiplier. 1.1 means 1.1x mana cost.")]
        private float manaCostIncreased = 1f;
        [SerializeField, Min(0.01f)]
        private float manaCostMultiplier = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe | SkillDefinitionTags.Targeted;

        void IAreaSizeModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.AreaSize, areaSizeAdded);
        void IAreaSizeModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.AreaSize, areaSizeIncreased);
        void IAreaSizeModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.AreaSize, areaSizeMultiplier);
        void IManaModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);
        void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.ManaCost, manaCostIncreased);
        void IManaModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);
    }
}
