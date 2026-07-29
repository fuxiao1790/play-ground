using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Piercing", fileName = "PiercingSupport")]
    public sealed class PiercingSupport : StatModifierSupport, IPierceCountModifiers.IBaseValueModifier, IManaModifiers.IBaseValueModifier, IProjectileBehaviorModifier
    {
        [SerializeField, Min(0)] private int pierceCount = 2;
        [SerializeField, Min(0f)] private float repeatHitCooldown = 0.5f;
        [SerializeField, Min(0f)] private float manaCostAdded = 3f;

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

        void IPierceCountModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.PierceCount, pierceCount);
        void IManaModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);

        public void ApplyToProjectile(ProjectileBehaviorContext ctx) => ctx.RepeatHitCooldown = repeatHitCooldown;
    }
}
