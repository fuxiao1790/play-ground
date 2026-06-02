using UnityEngine;

namespace PlayGround.Common.StatusEffects
{
    [CreateAssetMenu(menuName = "PlayGround/Status Effects/Stacking Trigger", fileName = "StackingTriggerDef")]
    public sealed class StackingTriggerDef : StatusEffectDef
    {
        [SerializeField, Min(1)] private int stackThreshold = 3;
        [SerializeField] private int triggerAoeTypeId = -1;
        [SerializeField] private float triggerAoeDamage;
        [SerializeField] private float triggerAoeLifetimeSeconds;
        [SerializeField] private float triggerAoeTickIntervalSeconds;

        public int StackThreshold => stackThreshold;
        public int TriggerAoeTypeId => triggerAoeTypeId;
        public float TriggerAoeDamage => triggerAoeDamage;
        public float TriggerAoeLifetimeSeconds => triggerAoeLifetimeSeconds;
        public float TriggerAoeTickIntervalSeconds => triggerAoeTickIntervalSeconds;
        public float DamageContributionPerStack => stackThreshold > 0 ? triggerAoeDamage / stackThreshold : 0f;

        public void Configure(
            int threshold,
            int aoeTypeId,
            float aoeDamage,
            float lifetimeSeconds = 0f,
            float tickIntervalSeconds = 0f)
        {
            stackThreshold = Mathf.Max(1, threshold);
            triggerAoeTypeId = aoeTypeId;
            triggerAoeDamage = Mathf.Max(0f, aoeDamage);
            triggerAoeLifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            triggerAoeTickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
        }
    }
}
