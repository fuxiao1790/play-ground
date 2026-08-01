using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple AOEs", fileName = "MultipleAoesSupport")]
    public sealed class MultipleAoesSupport : StatModifierSupport,
        IManaModifiers.IBaseValueModifier,
        IManaModifiers.IIncreasedModifier,
        IManaModifiers.IMultiplierModifier,
        IAoeBehaviorModifier
    {
        [SerializeField, Min(1)] private int echoCount = 3;
        [SerializeField, Min(0f)] private float scatterRadius = 2f;
        [SerializeField, Min(0f)] private float manaCostAdded = 4f;
        [SerializeField, FormerlySerializedAs("manaCostIncreasedPercent")]
        private float manaCostIncreased;
        [SerializeField, Min(0f)] private float manaCostMultiplier = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);
        public void CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.ManaCost, manaCostIncreased);
        public void CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);

        public void ApplyToAoe(AoeBehaviorContext ctx)
        {
            ctx.EchoCount += echoCount;
            ctx.ScatterRadius += scatterRadius;
        }
    }
}
