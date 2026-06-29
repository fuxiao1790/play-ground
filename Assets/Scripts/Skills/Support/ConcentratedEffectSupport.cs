using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Concentrated Effect", fileName = "ConcentratedEffectSupport")]
    public sealed class ConcentratedEffectSupport : StatModifierSupport, IMultiplierModifier
    {
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
        private float areaSizeMultiplier = 0.75f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public void CollectMultipliers(MultiplierSink sink)
        {
            sink.Add(SkillStat.AreaSize, areaSizeMultiplier);
        }
    }
}
