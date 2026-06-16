using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Impact Projectile", fileName = "NewOnImpactProjectileTrigger")]
    public sealed class OnImpactProjectileTrigger : TriggerLink
    {
        [SerializeField] public float spreadDegrees = 30f;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Projectile;
    }
}
