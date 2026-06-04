using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/Projectile Config", fileName = "ProjectileConfig")]
    public sealed class ProjectileConfig : ScriptableObject
    {
        [SerializeField] private BasicAttackPrefab basicPrefab;
        [SerializeField] private float speed = 16f;
        [SerializeField] private float lifetime = 1.5f;
        [SerializeField] private float damage = 10f;
        [SerializeField] private int count = 1;
        [SerializeField] private float spreadDegrees;
        [SerializeField] private float jitterDegrees;
        [SerializeField] private int targetMask = 1;
        [SerializeField] private int pierceCount;
        [SerializeField] private float repeatHitCooldownSeconds;
        [SerializeField] private bool trackingEnabled;
        [SerializeField] private float trackingRange;
        [SerializeField] private float trackingTurnSpeedDegrees;
        [SerializeField] private float trackingQueryIntervalSeconds;
        [SerializeField] private float trackingInitialQueryDelaySeconds;
        [SerializeField] private bool directDamageEnabled = true;
        [SerializeField] private AoeConfig impactAoeConfig;

        public BasicAttackPrefab Prefab => basicPrefab;
        public float Speed => speed;
        public float Lifetime => lifetime;
        public float Damage => damage;
        public int Count => count;
        public float SpreadDegrees => spreadDegrees;
        public float JitterDegrees => jitterDegrees;
        public int TargetMask => targetMask;
        public int PierceCount => pierceCount;
        public float RepeatHitCooldown => repeatHitCooldownSeconds;
        public bool DirectDamageEnabled => directDamageEnabled;
        public AoeConfig ImpactAoeConfig => impactAoeConfig;

        public ProjectileTrackingConfig GetTrackingConfig() =>
            new ProjectileTrackingConfig(
                trackingEnabled,
                trackingRange,
                trackingTurnSpeedDegrees,
                trackingQueryIntervalSeconds,
                trackingInitialQueryDelaySeconds);
    }
}
