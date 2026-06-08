using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple Projectiles", fileName = "MultipleProjectilesSupport")]
    public sealed class MultipleProjectilesSupport : AdditiveSupport
    {
        [SerializeField, Min(1)] private int count = 3;
        [SerializeField, Min(0f)] private float spreadDegrees = 30f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
            {
                p.count = count;
                p.spreadDegrees = spreadDegrees;
            }
        }
    }
}
