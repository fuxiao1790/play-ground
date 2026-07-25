using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple AOEs", fileName = "MultipleAoesSupport")]
    public sealed class MultipleAoesSupport : StatModifierSupport, IBaseValueModifier, IAoeBehaviorModifier
    {
        [SerializeField, Min(1)] private int echoCount = 3;
        [SerializeField, Min(0f)] private float scatterRadius = 2f;
        [SerializeField, Min(0f)] private float manaCostAdded = 4f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public void CollectAdded(AddedSink sink)
        {
            sink.Add(SkillStat.ManaCost, manaCostAdded);
        }

        public void ApplyToAoe(AoeBehaviorContext ctx)
        {
            ctx.EchoCount += echoCount;
            ctx.ScatterRadius += scatterRadius;
        }
    }
}
