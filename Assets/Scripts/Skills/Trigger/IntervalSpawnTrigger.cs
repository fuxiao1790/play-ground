using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    public abstract class IntervalSpawnTrigger : TriggerLink
    {
        // Energy gained per second by a duration source (projectile / lingering AOE).
        [Min(0.01f)] public float energyPerSecond = 2f;
        [FormerlySerializedAs("intervalJitterPercent")]
        [Range(0f, 100f)] public float energyJitterPercent;
        // Child spawn energy cost = child manaCost * this multiplier. Mana cost is not
        // deducted for interval spawns (internal spawns ignore cost); it only scales
        // the per-child energy threshold here.
        [FormerlySerializedAs("manaToEnergyRatio")]
        [Min(0f)] public float manaToEnergyCostMultiplier = 1f;

        public float ManaToEnergyCost(float manaCost) =>
            Mathf.Max(1e-3f, manaCost * manaToEnergyCostMultiplier);
    }
}
