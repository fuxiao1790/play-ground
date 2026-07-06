using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple AOEs", fileName = "MultipleAoesSupport")]
    public sealed class MultipleAoesSupport : StatModifierSupport, IAoeBehaviorModifier
    {
        [SerializeField, Min(1)] private int echoCount = 3;
        [SerializeField, Min(0f)] private float scatterRadius = 2f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

        public void ApplyToAoe(AoeBehaviorContext ctx)
        {
            ctx.EchoCount += echoCount;
            ctx.ScatterRadius += scatterRadius;
        }
    }
}
