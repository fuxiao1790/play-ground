using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Added Damage", fileName = "AddedDamageSupport")]
    public sealed class AddedDamageSupport : StatModifierSupport, IBaseValueModifier
    {
        [SerializeField] private float addedDamage = 5f;
        [SerializeField, Min(0f)] private float manaCostAdded;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        public void CollectAdded(AddedSink sink)
        {
            sink.Add(SkillStat.Damage, addedDamage);
            sink.Add(SkillStat.ManaCost, manaCostAdded);
        }
    }
}
