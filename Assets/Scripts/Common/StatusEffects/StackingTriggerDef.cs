using PlayGround.Attack;
using UnityEngine;

namespace PlayGround.Common.StatusEffects
{
    [CreateAssetMenu(menuName = "PlayGround/Status Effects/Stacking Trigger", fileName = "StackingTriggerDef")]
    public sealed class StackingTriggerDef : StatusEffectDef
    {
        [SerializeField, Min(1)] private int stackThreshold = 3;
        [SerializeField] private AoeConfig triggerAoeConfig;
        [SerializeField] private float triggerAoeDamage;
        [SerializeField] private float triggerAoeLifetimeSeconds;
        [SerializeField] private float triggerAoeTickIntervalSeconds;

        public int StackThreshold => stackThreshold;
        public AoeConfig TriggerAoeConfig => triggerAoeConfig;
        public float TriggerAoeDamage => triggerAoeDamage;
        public float TriggerAoeLifetimeSeconds => triggerAoeLifetimeSeconds;
        public float TriggerAoeTickIntervalSeconds => triggerAoeTickIntervalSeconds;
        public float DamageContributionPerStack => stackThreshold > 0 ? triggerAoeDamage / stackThreshold : 0f;

        public void Configure(
            int threshold,
            AoeConfig aoeConfig,
            float aoeDamage,
            float lifetimeSeconds = 0f,
            float tickIntervalSeconds = 0f)
        {
            stackThreshold = Mathf.Max(1, threshold);
            triggerAoeConfig = aoeConfig;
            triggerAoeDamage = Mathf.Max(0f, aoeDamage);
            triggerAoeLifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            triggerAoeTickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
        }
    }
}
