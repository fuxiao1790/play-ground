using PlayGround.Common.Modifiers;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Interval Spawn", fileName = "NewIntervalSpawnTrigger")]
    public sealed class IntervalSpawnTrigger : TriggerLink
    {
        // Energy gained per second by a duration source (projectile / lingering AOE).
        [Min(0.01f)] public float energyPerSecond = 2f;
        // Each source starts with a deterministic random charge from -this to +this percentage of its threshold.
        [FormerlySerializedAs("initialEnergy"), Range(0f, 100f)] public float initialEnergyPercent;

        public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Interval;
        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Any;

        public float ResolveEnergyPerSecond(SkillStatSnapshot snapshot) =>
            Mathf.Max(0.01f, StatFold.Resolve(
                baseValue: energyPerSecond,
                addedBase: snapshot.BaseEnergyGain,
                increased: snapshot.IncreasedEnergyGain,
                multiplier: snapshot.EnergyGainMultiplier));

        public float ResolveInitialEnergyPercent() => Mathf.Clamp(initialEnergyPercent, 0f, 100f);

        public float ResolveInitialEnergyMaximum(float energyThreshold) =>
            Mathf.Max(0f, energyThreshold) * (ResolveInitialEnergyPercent() * 0.01f);

        public float ManaToEnergyCost(float manaCost) =>
            Mathf.Max(1e-3f, manaCost * ResolveManaCostFactor());
    }
}
