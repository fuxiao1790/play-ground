using UnityEngine;

namespace PlayGround.Skills
{
    public abstract class IntervalSpawnTrigger : TriggerLink
    {
        // Energy gained per second by a duration source (projectile / lingering AOE).
        [Min(0.01f)] public float energyPerSecond = 2f;

        public float ManaToEnergyCost(float manaCost) =>
            Mathf.Max(1e-3f, manaCost * manaCostMultiplier);
    }
}
