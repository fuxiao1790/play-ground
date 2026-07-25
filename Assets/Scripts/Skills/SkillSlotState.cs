using UnityEngine;

namespace PlayGround.Skills
{
    public sealed class SkillSlotState
    {
        private float elapsedSinceLastFire;
        private float recoveryTime = 0.2f;

        public bool IsReady => elapsedSinceLastFire >= recoveryTime;
        public float CooldownProgress => recoveryTime > 0f
            ? Mathf.Clamp01(elapsedSinceLastFire / recoveryTime)
            : 1f;

        public void SetRecoveryTime(float seconds)
        {
            recoveryTime = Mathf.Max(0.01f, seconds);
        }

        public void Tick(float deltaTime)
        {
            if (elapsedSinceLastFire < recoveryTime)
                elapsedSinceLastFire = Mathf.Min(elapsedSinceLastFire + deltaTime, recoveryTime);
        }

        public void ResetOnFire()
        {
            elapsedSinceLastFire = 0f;
        }

        public void RefundFire()
        {
            elapsedSinceLastFire = recoveryTime;
        }
    }
}
