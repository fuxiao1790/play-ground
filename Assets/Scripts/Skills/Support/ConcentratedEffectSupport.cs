using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Concentrated Effect", fileName = "ConcentratedEffectSupport")]
    public sealed class ConcentratedEffectSupport : AdditiveSupport
    {
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
        private float areaSizeMultiplier = 0.75f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public override void Apply(SkillDefinition def)
        {
            if (def is AoeDefinitionBase a)
            {
                a.baseAreaSize *= areaSizeMultiplier;
            }
        }
    }
}
