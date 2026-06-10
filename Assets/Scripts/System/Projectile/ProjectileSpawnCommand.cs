using PlayGround.Common;
using PlayGround.System.Common;
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
            CombatShapeType shapeType)
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

        [global::System.Obsolete("Use the CombatShapeType overload.")]
        public ProjectileSpawnCommand(
            Vector2 position,
            Vector2 direction,
            float speed,
            float lifetime,
            float radius,
            DamageSnapshot damage,
            ProjectileShapeType shapeType)
            : this(position, direction, speed, lifetime, radius, damage, (CombatShapeType)(int)shapeType)
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
            CombatShapeType shapeType,
            int projectileTypeId = 0,
            int targetMask = 1,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnConfig childSpawn = default,
            bool directDamageEnabled = true,
            EntityId sourceNodeId = default,
            ProjectileImpactAoeSnapshot impactAoe = default,
            CombatStackEffectSnapshot stackEffect = default,
            ProjectileImpactProjectileSnapshot impactProjectile = default,
            float critChance = 0f,
            float critMultiplier = 1.5f)
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
            ImpactAoe = impactAoe;
            HitPayload = new ProjectileHitPayload(sourceNodeId, damage.Amount, directDamageEnabled, impactAoe, stackEffect, impactProjectile, critChance, critMultiplier);
        }

        [global::System.Obsolete("Use the CombatShapeType overload.")]
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
            bool directDamageEnabled = true,
            EntityId sourceNodeId = default,
            ProjectileImpactAoeSnapshot impactAoe = default)
            : this(
                position,
                direction,
                speed,
                lifetime,
                radius,
                halfExtents,
                rotationRadians,
                damage,
                (CombatShapeType)(int)shapeType,
                projectileTypeId,
                targetMask,
                pierceCount,
                repeatHitCooldownSeconds,
                tracking,
                childSpawn,
                directDamageEnabled,
                sourceNodeId,
                impactAoe)
        {
        }

        public Vector2 Position { get; }
        public Vector2 Direction { get; }
        public float Speed { get; }
        public float Lifetime { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public float RotationRadians { get; }
        public DamageSnapshot Damage { get; }
        public CombatShapeType ShapeType { get; }
        public int ProjectileTypeId { get; }
        public int TargetMask { get; }
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
        public ProjectileTrackingConfig Tracking { get; }
        public ProjectileChildSpawnConfig ChildSpawn { get; }
        public bool DirectDamageEnabled { get; }
        public ProjectileImpactAoeSnapshot ImpactAoe { get; }
        public ProjectileHitPayload HitPayload { get; }
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

    public enum ProjectileChildSpawnPatternType
    {
        SideSpray = 0,
        Forward = 1,
    }

    public readonly struct ProjectileChildSpawnBehavior
    {
        public static readonly ProjectileChildSpawnBehavior Default = new(1, ProjectileChildSpawnPatternType.SideSpray);

        public ProjectileChildSpawnBehavior(
            int count,
            ProjectileChildSpawnPatternType pattern = ProjectileChildSpawnPatternType.SideSpray,
            float spreadDegrees = 30f)
        {
            Count = Mathf.Max(1, count);
            PatternType = pattern;
            SpreadDegrees = Mathf.Max(0f, spreadDegrees);
        }

        public int Count { get; }
        public ProjectileChildSpawnPatternType PatternType { get; }
        public float SpreadDegrees { get; }
    }

    public readonly struct ProjectileChildSpawnConfig
    {
        public static readonly ProjectileChildSpawnConfig Disabled = default;

        public ProjectileChildSpawnConfig(
            int spawnerId,
            int typeId,
            float intervalSeconds,
            float intervalJitterSeconds,
            float speed,
            float lifetime,
            float radius,
            Vector2 halfExtents,
            CombatShapeType shapeType,
            float rotationRadians,
            DamageSnapshot damage,
            int targetMask = 0,
            bool directDamageEnabled = true,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            float visualScale = 1f,
            float visualRotationDegrees = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnBehavior behavior = default,
            ProjectileImpactAoeSnapshot impactAoe = default,
            CombatStackEffectSnapshot stackEffect = default,
            ProjectileImpactProjectileSnapshot impactProjectile = default)
        {
            SpawnerId = spawnerId;
            TypeId = typeId;
            IntervalSeconds = Mathf.Max(0f, intervalSeconds);
            IntervalJitterSeconds = Mathf.Max(0f, intervalJitterSeconds);
            Speed = Mathf.Max(0f, speed);
            Lifetime = Mathf.Max(0f, lifetime);
            Radius = Mathf.Max(0f, radius);
            HalfExtents = halfExtents;
            ShapeType = shapeType;
            RotationRadians = rotationRadians;
            Damage = damage;
            TargetMask = targetMask;
            DirectDamageEnabled = directDamageEnabled;
            PierceCount = Mathf.Max(0, pierceCount);
            RepeatHitCooldownSeconds = Mathf.Max(0f, repeatHitCooldownSeconds);
            VisualScale = Mathf.Max(0f, visualScale);
            VisualRotationDegrees = visualRotationDegrees;
            Tracking = tracking;
            Behavior = behavior;
            ImpactAoe = impactAoe;
            StackEffect = stackEffect;
            ImpactProjectile = impactProjectile;
        }

        [global::System.Obsolete("Use the CombatShapeType overload.")]
        public ProjectileChildSpawnConfig(
            int spawnerId,
            int typeId,
            float intervalSeconds,
            float intervalJitterSeconds,
            float speed,
            float lifetime,
            float radius,
            Vector2 halfExtents,
            ProjectileShapeType shapeType,
            float rotationRadians,
            DamageSnapshot damage,
            int targetMask = 0,
            bool directDamageEnabled = true,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            float visualScale = 1f,
            float visualRotationDegrees = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnBehavior behavior = default)
            : this(
                spawnerId,
                typeId,
                intervalSeconds,
                intervalJitterSeconds,
                speed,
                lifetime,
                radius,
                halfExtents,
                (CombatShapeType)(int)shapeType,
                rotationRadians,
                damage,
                targetMask,
                directDamageEnabled,
                pierceCount,
                repeatHitCooldownSeconds,
                visualScale,
                visualRotationDegrees,
                tracking,
                behavior)
        {
        }

        public int SpawnerId { get; }
        public int TypeId { get; }
        public float IntervalSeconds { get; }
        public float IntervalJitterSeconds { get; }
        public float Speed { get; }
        public float Lifetime { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public CombatShapeType ShapeType { get; }
        public float RotationRadians { get; }
        public DamageSnapshot Damage { get; }
        public int TargetMask { get; }
        public bool DirectDamageEnabled { get; }
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
        public float VisualScale { get; }
        public float VisualRotationDegrees { get; }
        public ProjectileTrackingConfig Tracking { get; }
        public ProjectileChildSpawnBehavior Behavior { get; }
        public ProjectileImpactAoeSnapshot ImpactAoe { get; }
        public CombatStackEffectSnapshot StackEffect { get; }
        public ProjectileImpactProjectileSnapshot ImpactProjectile { get; }
        public bool Enabled => SpawnerId > 0 && IntervalSeconds > 0f;
    }
}
