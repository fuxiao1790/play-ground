using UnityEngine;
using UnityEngine.Serialization;

namespace PlayGround.Skills
{
    public abstract class IntervalSpawnTrigger : TriggerLink
    {
        [Min(0.01f)] public float energyPerSecond = 2f;
        [FormerlySerializedAs("intervalJitterPercent")]
        [Range(0f, 100f)] public float energyJitterPercent;
        [Min(0.0001f)] public float manaToEnergyRatio = 1f;

        public float ManaToEnergyCost(float manaCost) =>
            Mathf.Max(1e-3f, manaCost * manaToEnergyRatio);
    }
}
