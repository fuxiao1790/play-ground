using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple Projectiles", fileName = "MultipleProjectilesSupport")]
    public sealed class MultipleProjectilesSupport : StatModifierSupport,
        IManaModifiers.IBaseValueModifier,
        IManaModifiers.IIncreasedModifier,
        IManaModifiers.IMultiplierModifier,
        IProjectileBehaviorModifier
    {
        [SerializeField, Min(1)] private int count = 3;
        [SerializeField, Min(0f)] private float spreadDegrees = 30f;
        [SerializeField, Min(0f)] private float manaCostAdded = 4f;
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent"), Min(0f), Tooltip("Direct mana-cost increase multiplier. 1.1 means 1.1x mana cost.")]
        private float manaCostIncreased = 1f;
        [SerializeField, Min(0f)] private float manaCostMultiplier = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);
        public void CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.ManaCost, manaCostIncreased);
        public void CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);

        public void ApplyToProjectile(ProjectileBehaviorContext ctx)
        {
            ctx.Count += count;
            ctx.SpreadDegrees += spreadDegrees;
        }
    }
}
