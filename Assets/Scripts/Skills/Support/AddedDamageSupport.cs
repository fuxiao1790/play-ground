using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Added Damage", fileName = "AddedDamageSupport")]
    public sealed class AddedDamageSupport : AdditiveSupport
    {
        [SerializeField] private float addedDamage = 5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
                p.damage += addedDamage;
            else if (def is AoeDefinitionBase a)
                a.damage += addedDamage;
        }
    }
}
