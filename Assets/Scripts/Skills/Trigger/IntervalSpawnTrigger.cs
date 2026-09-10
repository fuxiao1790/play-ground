using PlayGround.Common.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Interval Spawn", fileName = "NewIntervalSpawnTrigger")]
    public sealed class IntervalSpawnTrigger : TriggerLink
    {
        // Energy gained per second by a duration source (projectile / lingering AOE).
        [Min(0.01f)] public float energyPerSecond = 2f;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Interval;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Any;

        public float ResolveEnergyPerSecond(SkillStatSnapshot snapshot) =>
            Mathf.Max(0.01f, StatFold.Resolve(
                baseValue: energyPerSecond,
                addedBase: snapshot.BaseEnergyGain,
                increased: snapshot.IncreasedEnergyGain,
                multiplier: snapshot.EnergyGainMultiplier));

        public float ManaToEnergyCost(float manaCost) =>
            Mathf.Max(1e-3f, manaCost * ResolveManaCostFactor());
    }
}
