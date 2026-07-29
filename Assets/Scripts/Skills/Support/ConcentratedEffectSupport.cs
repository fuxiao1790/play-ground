using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Concentrated Effect", fileName = "ConcentratedEffectSupport")]
    public sealed class ConcentratedEffectSupport : StatModifierSupport, IAreaSizeModifiers.IMultiplierModifier, IManaModifiers.IMultiplierModifier
    {
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
        private float areaSizeMultiplier = 0.75f;
        [SerializeField, Min(0f)] private float manaCostMultiplier = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        void IAreaSizeModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.AreaSize, areaSizeMultiplier);
        void IManaModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);
    }
}
