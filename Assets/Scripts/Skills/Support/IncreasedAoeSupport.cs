using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Aoe Effect", fileName = "IncreasedAoeSupport")]
    public sealed class IncreasedAoeSupport : AdditiveSupport
    {
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
        private float areaSizeMultiplier = 1.5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public override void Apply(SkillDefinition def)
        {
            Apply(def, def);
        }

        public override void Apply(SkillDefinition def, SkillDefinition baseDefinition)
        {
            if (def is AoeDefinitionBase a && baseDefinition is AoeDefinitionBase baseAoe)
            {
                float addedAreaSize = baseAoe.baseAreaSize * (areaSizeMultiplier - 1f);
                a.baseAreaSize += addedAreaSize;
            }
        }
    }
}
