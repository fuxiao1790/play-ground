using PlayGround.Common;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.System.Combat.Projectiles
{
    public readonly struct ProjectileSpawnRequest
    {
        public ProjectileSpawnRequest(
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
                0,
                0f,
                ProjectileTrackingConfig.Disabled,
                ProjectileChildSpawnConfig.Disabled,
                true)
        {
        }

        [global::System.Obsolete("Use the CombatShapeType overload.")]
        public ProjectileSpawnRequest(
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

        public ProjectileSpawnRequest(
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
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnConfig childSpawn = default,
            bool directDamageEnabled = true,
            EntityId sourceNodeId = default,
            StackEffectSnapshot stackEffect = default,
            float critChance = 0f,
            float critMultiplier = 1.5f,
            int count = 1,
            float spreadDegrees = 0f,
            float jitterDegrees = 0f,
            TimedSpawnComponent timedSpawn = default,
            IntervalChildKind childKind = IntervalChildKind.Projectile,
            OnHitSpawnRef onHitSpawn = default)
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
            PierceCount = Mathf.Max(0, pierceCount);
            RepeatHitCooldownSeconds = Mathf.Max(0f, repeatHitCooldownSeconds);
            Tracking = tracking;
            ChildSpawn = childSpawn;
            DirectDamageEnabled = directDamageEnabled;
            HitPayload = new ProjectileHitPayload(
                new CombatHitPayload
                {
                    DamageAmount = damage.Amount,
                    CritChance = critChance,
                    CritMultiplier = critMultiplier,
                    DirectDamageEnabled = directDamageEnabled,
                    SourceNodeId = sourceNodeId,
                    StackEffect = stackEffect
                },
                onHitSpawn);
            Count = Mathf.Max(1, count);
            SpreadDegrees = Mathf.Max(0f, spreadDegrees);
            JitterDegrees = Mathf.Max(0f, jitterDegrees);
            TimedSpawn = timedSpawn;
            ChildKind = childKind;
        }

        [global::System.Obsolete("Use the CombatShapeType overload.")]
        public ProjectileSpawnRequest(
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
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnConfig childSpawn = default,
            bool directDamageEnabled = true,
            EntityId sourceNodeId = default)
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
                pierceCount,
                repeatHitCooldownSeconds,
                tracking,
                childSpawn,
                directDamageEnabled,
                sourceNodeId)
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
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
        public ProjectileTrackingConfig Tracking { get; }
        public ProjectileChildSpawnConfig ChildSpawn { get; }
        public TimedSpawnComponent TimedSpawn { get; }
        public IntervalChildKind ChildKind { get; }
        public bool DirectDamageEnabled { get; }
        public ProjectileHitPayload HitPayload { get; }
        public int Count { get; }
        public float SpreadDegrees { get; }
        public float JitterDegrees { get; }
    }

    public enum ProjectileChildSpawnPatternType
    {
        SideSpray = 0,
        Forward = 1,
        Radial = 2,
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
            int jitterSeed,
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
            bool directDamageEnabled = true,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            float visualScale = 1f,
            float visualRotationDegrees = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnBehavior behavior = default,
            StackEffectSnapshot stackEffect = default,
            Hash128 templateKey = default,
            OnHitSpawnRef onHitSpawn = default)
        {
            JitterSeed = jitterSeed;
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
            DirectDamageEnabled = directDamageEnabled;
            PierceCount = Mathf.Max(0, pierceCount);
            RepeatHitCooldownSeconds = Mathf.Max(0f, repeatHitCooldownSeconds);
            VisualScale = Mathf.Max(0f, visualScale);
            VisualRotationDegrees = visualRotationDegrees;
            Tracking = tracking;
            Behavior = behavior;
            StackEffect = stackEffect;
            TemplateKey = templateKey;
        }

        [global::System.Obsolete("Use the CombatShapeType overload.")]
        public ProjectileChildSpawnConfig(
            int jitterSeed,
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
            bool directDamageEnabled = true,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            float visualScale = 1f,
            float visualRotationDegrees = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileChildSpawnBehavior behavior = default)
            : this(
                jitterSeed,
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
                directDamageEnabled,
                pierceCount,
                repeatHitCooldownSeconds,
                visualScale,
                visualRotationDegrees,
                tracking,
                behavior)
        {
        }

        public int JitterSeed { get; }
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
        public bool DirectDamageEnabled { get; }
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
        public float VisualScale { get; }
        public float VisualRotationDegrees { get; }
        public ProjectileTrackingConfig Tracking { get; }
        public ProjectileChildSpawnBehavior Behavior { get; }
        public StackEffectSnapshot StackEffect { get; }
        public Hash128 TemplateKey { get; }
        public bool Enabled => JitterSeed > 0 && IntervalSeconds > 0f;
    }
}
