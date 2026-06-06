using PlayGround.System.Aoe;
using UnityEngine;

namespace PlayGround.Attack
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/Lingering AOE Config", fileName = "LingeringAoeConfig")]
    public sealed class LingeringAoeConfig : AoeConfig
    {
        [SerializeField, Min(0f)] private float lifetimeSeconds;
        [SerializeField, Min(0f)] private float tickIntervalSeconds;
        [SerializeField] private LingeringAoePrefab lingeringPrefab;

        public override float LifetimeSeconds => lifetimeSeconds;
        public override float TickIntervalSeconds => tickIntervalSeconds;

        public void Configure(LingeringAoePrefab prefab)
        {
            lingeringPrefab = prefab;
        }

        public override AoeTypeDefinition CreateTypeDefinition()
        {
            var definition = new AoeTypeDefinition();
            definition.Configure(
                lingeringPrefab != null ? lingeringPrefab.gameObject : null,
                lingeringPrefab != null ? lingeringPrefab.Hurtbox : null,
                SizeMultiplier,
                lingeringPrefab != null ? lingeringPrefab.VisualRotationDegrees : 0f,
                PreloadCount,
                lingeringPrefab != null ? lingeringPrefab.SpawnEffect : null,
                lingeringPrefab != null ? lingeringPrefab.HitEffect : null,
                lingeringPrefab != null ? lingeringPrefab.ExpireEffect : null,
                lingeringPrefab != null ? lingeringPrefab.PulseEffect : null);
            return definition;
        }

        public override bool IsValidConfig(out string reason)
        {
            if (lingeringPrefab == null)
            {
                reason = "lingeringPrefab is not assigned";
                return false;
            }

            return lingeringPrefab.IsValidTemplate(out reason);
        }
    }
}
