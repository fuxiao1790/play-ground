using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Duration", fileName = "IncreasedDurationSupport")]
    public sealed class IncreasedDurationSupport : StatModifierSupport,
        IDurationModifiers.IBaseValueModifier,
        IDurationModifiers.IIncreasedModifier,
        IDurationModifiers.IMultiplierModifier
    {
        [SerializeField, Min(0f)] private float durationAdded = 1f;
        [SerializeField, Min(0f), Tooltip("Direct duration increase multiplier. 1.2 means 1.2x duration.")]
        private float durationIncreased = 1.2f;
        [SerializeField, Min(0.01f)] private float durationMultiplier = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Duration;

        public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.Duration, durationAdded);
        public void CollectIncreases(IncreasedSink sink) => sink.AddFactor(SkillStat.Duration, durationIncreased);
        public void CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.Duration, durationMultiplier);
    }
}
