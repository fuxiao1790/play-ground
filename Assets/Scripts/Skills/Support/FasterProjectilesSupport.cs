using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Faster Projectiles", fileName = "FasterProjectilesSupport")]
    public sealed class FasterProjectilesSupport : StatModifierSupport, IProjectileSpeedModifiers.IMultiplierModifier, IDurationModifiers.IMultiplierModifier, IManaModifiers.IMultiplierModifier
    {
        [SerializeField, Min(0.01f)] private float speedMultiplier = 1.5f;
        [SerializeField, Min(0.01f)] private float lifetimeMultiplier = 1.2f;
        [SerializeField, Min(0f)] private float manaCostMultiplier = 1f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        void IProjectileSpeedModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ProjectileSpeed, speedMultiplier);
        void IDurationModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.Duration, lifetimeMultiplier);
        void IManaModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);
    }
}
