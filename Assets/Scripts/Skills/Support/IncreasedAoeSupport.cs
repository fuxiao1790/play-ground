using PlayGround.Skills.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Aoe Effect", fileName = "IncreasedAoeSupport")]
    public sealed class IncreasedAoeSupport : StatModifierSupport, IIncreasedModifier
    {
        [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
        private float areaSizeMultiplier = 1.5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public void CollectIncreases(IncreasedSink sink)
        {
            sink.Add(SkillStat.AreaSize, areaSizeMultiplier - 1f);
        }
    }
}
