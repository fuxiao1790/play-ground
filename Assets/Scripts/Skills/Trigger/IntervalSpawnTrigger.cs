using PlayGround.Common.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class IntervalSpawnTrigger : TriggerLink
    {
        // Energy gained per second by a duration source (projectile / lingering AOE).
        [Min(0.01f)] public float energyPerSecond = 2f;

        public float ResolveEnergyPerSecond(SkillStatSnapshot snapshot) =>
            Mathf.Max(0.01f, StatFold.Resolve(
                baseValue: energyPerSecond,
                added: snapshot.BaseEnergyGain,
                increasedPercent: snapshot.IncreasedEnergyGainPercent,
                multiplier: snapshot.EnergyGainMultiplier));

        public float ManaToEnergyCost(float manaCost) =>
            Mathf.Max(1e-3f, manaCost * ResolveManaCostFactor());
    }
}
