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
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent")]
        private float manaCostIncreased;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        void IAreaSizeModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.AreaSize, areaSizeMultiplier - 1f);
        void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.ManaCost, manaCostIncreased);
    }
}
