using UnityEngine;

namespace PlayGround.Mob
{
    public sealed class MobBlackboard
    {
        private int requestedTriggerPriority = -1;

        public Transform Target { get; set; }
        public bool TargetVisible { get; set; }
        public MobBehaviourState BehaviourState { get; set; } = MobBehaviourState.Idle;
        public float Health { get; set; } = 1f;
        public float MaxHealth { get; set; } = 1f;
        public string RequestedTriggerKey { get; private set; } = string.Empty;
        public string ActiveTriggerKey { get; set; } = "none";
        public string ActiveBehaviourKey { get; set; } = "none";

        public void BeginFrame(string defaultTriggerKey)
        {
            RequestedTriggerKey = defaultTriggerKey;
            ActiveTriggerKey = "none";
            requestedTriggerPriority = string.IsNullOrEmpty(defaultTriggerKey) ? -1 : -100;
        }

        public void RequestTrigger(string key, int priority = 0)
        {
            if (string.IsNullOrEmpty(key) || priority < requestedTriggerPriority)
            {
                return;
            }

            RequestedTriggerKey = key;
            requestedTriggerPriority = priority;
        }

        public bool HasValidTarget()
        {
            return Target != null && Target.gameObject.activeInHierarchy;
        }

        public void ClearTarget()
        {
            Target = null;
            TargetVisible = false;
        }

        public float HealthRatio()
        {
            return MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / MaxHealth);
        }
    }
}
