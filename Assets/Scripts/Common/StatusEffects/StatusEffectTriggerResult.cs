using UnityEngine;

namespace PlayGround.Common.StatusEffects
{
    public readonly struct StatusEffectTriggerResult
    {
        public StatusEffectTriggerResult(int triggerCount, float totalTriggerDamage, Vector2 ownerPosition)
        {
            TriggerCount = triggerCount;
            TotalTriggerDamage = totalTriggerDamage;
            OwnerPosition = ownerPosition;
        }

        public bool Triggered => TriggerCount > 0;
        public int TriggerCount { get; }
        public float TotalTriggerDamage { get; }
        public Vector2 OwnerPosition { get; }
    }
}
