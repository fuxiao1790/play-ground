using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Piercing", fileName = "PiercingSupport")]
    public sealed class PiercingSupport : AdditiveSupport
    {
        [SerializeField, Min(0)] private int pierceCount = 2;
        [SerializeField, Min(0f)] private float repeatHitCooldown = 0.5f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        public override void Apply(SkillDefinition def)
        {
            if (def is ProjectileDefinition p)
            {
                p.pierceCount = pierceCount;
                p.repeatHitCooldown = repeatHitCooldown;
            }
        }
    }
}
