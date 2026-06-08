using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Faster Projectiles", fileName = "FasterProjectilesSupport")]
    public sealed class FasterProjectilesSupport : AdditiveSupport
    {
        [SerializeField, Min(0.01f)] private float speedMultiplier = 1.5f;
        [SerializeField, Min(0.01f)] private float lifetimeMultiplier = 1.2f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
            {
                p.speed *= speedMultiplier;
                p.lifetime *= lifetimeMultiplier;
            }
        }
    }
}
