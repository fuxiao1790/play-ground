using UnityEngine;

namespace PlayGround.System.Common
{
    public readonly struct ProjectileTrackingConfig
    {
        public static readonly ProjectileTrackingConfig Disabled = new(false, 0f, 0f, 0f);

        public ProjectileTrackingConfig(
            bool enabled,
            float turnSpeedDegrees,
            float queryIntervalSeconds,
            float initialQueryDelaySeconds = 0f)
        {
            Enabled = enabled;
            TurnSpeedDegrees = Mathf.Max(0f, turnSpeedDegrees);
            QueryIntervalSeconds = Mathf.Max(0f, queryIntervalSeconds);
            InitialQueryDelaySeconds = Mathf.Max(0f, initialQueryDelaySeconds);
        }

        public bool Enabled { get; }
        public float TurnSpeedDegrees { get; }
        public float QueryIntervalSeconds { get; }
        public float InitialQueryDelaySeconds { get; }
    }
}
