using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Concentrated Effect", fileName = "ConcentratedEffectSupport")]
    public sealed class ConcentratedEffectSupport : AdditiveSupport
    {
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1.5f;
        [SerializeField] private float addedDamage = 5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public override void Apply(SkillDefinition def)
        {
            if (def is AoeDefinitionBase a)
            {
                a.sizeMultiplier *= sizeMultiplier;
                a.damage += addedDamage;
            }
        }
    }
}
