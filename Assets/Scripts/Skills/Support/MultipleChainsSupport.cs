using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple Chains", fileName = "MultipleChainsSupport")]
    public sealed class MultipleChainsSupport : StatModifierSupport,
        IManaModifiers.IBaseValueModifier,
        IManaModifiers.IIncreasedModifier,
        IManaModifiers.IMultiplierModifier,
        ITargetedBehaviorModifier
    {
        [SerializeField, Min(1)] private int count = 2;
        [SerializeField, Min(0f)] private float manaCostAdded = 4f;
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent"), Min(0f)] private float manaCostIncreased = 1f;
        [SerializeField, Min(0f)] private float manaCostMultiplier = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Targeted;

        public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);
        public void CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.ManaCost, manaCostIncreased);
        public void CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);
        public void ApplyToTargeted(TargetedBehaviorContext ctx) => ctx.Count += count;
    }
}
