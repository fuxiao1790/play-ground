using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public readonly struct ProjectileSpawnCommand
    {
        public ProjectileSpawnCommand(
            Vector2 position,
            Vector2 direction,
            float speed,
            float lifetime,
            float radius,
            DamageSnapshot damage,
            ProjectileShapeType shapeType)
            : this(
                position,
                direction,
                speed,
                lifetime,
                radius,
                new Vector2(radius, radius),
                0f,
                damage,
                shapeType,
                0,
                1,
                0,
                0f,
                ProjectileTrackingConfig.Disabled,
                ProjectileChildSpawnConfig.Disabled,
                true)
        {
        }

        public ProjectileSpawnCommand(
            Vector2 position,
            Vector2 direction,
            float speed,
            float lifetime,
            float radius,
            Vector2 halfExtents,
            float rotationRadians,
            DamageSnapshot damage,
            ProjectileShapeType shapeType,
            int projectileTypeId = 0,
            int targetMask = 1,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnConfig childSpawn = default,
            bool directDamageEnabled = true)
        {
            Position = position;
            Direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.right;
            Speed = speed;
            Lifetime = lifetime;
            Radius = radius;
            HalfExtents = halfExtents;
            RotationRadians = rotationRadians;
            Damage = damage;
            ShapeType = shapeType;
            ProjectileTypeId = projectileTypeId;
            TargetMask = targetMask;
            PierceCount = Mathf.Max(0, pierceCount);
            RepeatHitCooldownSeconds = Mathf.Max(0f, repeatHitCooldownSeconds);
            Tracking = tracking;
            ChildSpawn = childSpawn;
            DirectDamageEnabled = directDamageEnabled;
        }

        public Vector2 Position { get; }
        public Vector2 Direction { get; }
        public float Speed { get; }
        public float Lifetime { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public float RotationRadians { get; }
        public DamageSnapshot Damage { get; }
        public ProjectileShapeType ShapeType { get; }
        public int ProjectileTypeId { get; }
        public int TargetMask { get; }
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
        public ProjectileTrackingConfig Tracking { get; }
        public ProjectileChildSpawnConfig ChildSpawn { get; }
        public bool DirectDamageEnabled { get; }
    }

    public readonly struct ProjectileTrackingConfig
    {
        public static readonly ProjectileTrackingConfig Disabled = new(false, 0f, 0f, 0f, 0f);

        public ProjectileTrackingConfig(
            bool enabled,
            float range,
            float turnSpeedDegrees,
            float queryIntervalSeconds,
            float initialQueryDelaySeconds = 0f)
        {
            Enabled = enabled;
            Range = Mathf.Max(0f, range);
            TurnSpeedDegrees = Mathf.Max(0f, turnSpeedDegrees);
            QueryIntervalSeconds = Mathf.Max(0f, queryIntervalSeconds);
            InitialQueryDelaySeconds = Mathf.Max(0f, initialQueryDelaySeconds);
        }

        public bool Enabled { get; }
        public float Range { get; }
        public float TurnSpeedDegrees { get; }
        public float QueryIntervalSeconds { get; }
        public float InitialQueryDelaySeconds { get; }
    }

    public readonly struct ProjectileChildSpawnConfig
    {
        public static readonly ProjectileChildSpawnConfig Disabled = new(0, 0f, 0f);

        public ProjectileChildSpawnConfig(int spawnerId, float intervalSeconds, float intervalJitterSeconds = 0f)
        {
            SpawnerId = spawnerId;
            IntervalSeconds = Mathf.Max(0f, intervalSeconds);
            IntervalJitterSeconds = Mathf.Max(0f, intervalJitterSeconds);
        }

        public int SpawnerId { get; }
        public float IntervalSeconds { get; }
        public float IntervalJitterSeconds { get; }
        public bool Enabled => SpawnerId > 0 && IntervalSeconds > 0f;
    }
}
