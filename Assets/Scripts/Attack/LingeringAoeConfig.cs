using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Attack
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/Lingering AOE Config", fileName = "LingeringAoeConfig")]
    public sealed class LingeringAoeConfig : AoeConfig
    {
        [SerializeField, Min(0f)] private float lifetimeSeconds;
        [SerializeField, Min(0f)] private float tickIntervalSeconds;
        [SerializeField] private VisualEffectAsset pulseEffect;

        public override float LifetimeSeconds => lifetimeSeconds;
        public override float TickIntervalSeconds => tickIntervalSeconds;
        protected override VisualEffectAsset PulseEffect => pulseEffect;
    }
}
