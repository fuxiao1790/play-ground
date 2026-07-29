using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple Projectiles", fileName = "MultipleProjectilesSupport")]
    public sealed class MultipleProjectilesSupport : StatModifierSupport, IManaModifiers.IBaseValueModifier, IProjectileBehaviorModifier
    {
        [SerializeField, Min(1)] private int count = 3;
        [SerializeField, Min(0f)] private float spreadDegrees = 30f;
        [SerializeField, Min(0f)] private float manaCostAdded = 4f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public void CollectAdded(AddedSink sink)
        {
            sink.Add(SkillStat.ManaCost, manaCostAdded);
        }

        public void ApplyToProjectile(ProjectileBehaviorContext ctx)
        {
            ctx.Count += count;
            ctx.SpreadDegrees += spreadDegrees;
        }
    }
}
