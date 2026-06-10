using PlayGround.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
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

    public readonly struct ProjectileImpactAoeSnapshot
    {
        public ProjectileImpactAoeSnapshot(
            int typeId,
            int targetMask,
            float damageAmount,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            float critChance = 0f,
            float critMultiplier = 1.5f)
        {
            Enabled = typeId >= 0;
            TypeId = typeId;
            TargetMask = targetMask;
            DamageAmount = damageAmount;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            CritChance = critChance;
            CritMultiplier = critMultiplier;
        }

        public int TypeId { get; }
        public int TargetMask { get; }
        public float DamageAmount { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public bool Enabled { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public DamageSnapshot Damage => new(Mathf.Max(0f, DamageAmount));
    }

    public readonly struct ProjectileImpactProjectileSnapshot
    {
        public ProjectileImpactProjectileSnapshot(
            int projectileTypeId,
            int targetMask,
            int count,
            float spreadDegrees,
            float speed,
            float lifetimeSeconds,
            float radius,
            Vector2 halfExtents,
            float rotationRadians,
            CombatShapeType shapeType,
            DamageSnapshot damage,
            bool directDamageEnabled = true,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f,
            ProjectileTrackingConfig tracking = default,
            ProjectileImpactAoeSnapshot impactAoe = default,
            CombatStackEffectSnapshot stackEffect = default)
        {
            Enabled = projectileTypeId >= 0;
            ProjectileTypeId = projectileTypeId;
            TargetMask = targetMask;
            Count = Mathf.Max(1, count);
            SpreadDegrees = Mathf.Max(0f, spreadDegrees);
            Speed = Mathf.Max(0f, speed);
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            Radius = Mathf.Max(0f, radius);
            HalfExtents = halfExtents;
            RotationRadians = rotationRadians;
            ShapeType = shapeType;
            Damage = damage;
            DirectDamageEnabled = directDamageEnabled;
            PierceCount = Mathf.Max(0, pierceCount);
            RepeatHitCooldownSeconds = Mathf.Max(0f, repeatHitCooldownSeconds);
            Tracking = tracking;
            ImpactAoe = impactAoe;
            StackEffect = stackEffect;
        }

        public bool Enabled { get; }
        public int ProjectileTypeId { get; }
        public int TargetMask { get; }
        public int Count { get; }
        public float SpreadDegrees { get; }
        public float Speed { get; }
        public float LifetimeSeconds { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public float RotationRadians { get; }
        public CombatShapeType ShapeType { get; }
        public DamageSnapshot Damage { get; }
        public bool DirectDamageEnabled { get; }
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
        public ProjectileTrackingConfig Tracking { get; }
        public ProjectileImpactAoeSnapshot ImpactAoe { get; }
        public CombatStackEffectSnapshot StackEffect { get; }
    }

    public readonly struct AoeProjectileBurstSnapshot
    {
        public AoeProjectileBurstSnapshot(
            int projectileTypeId,
            int targetMask,
            int count,
            float spreadDegrees,
            float speed,
            float lifetimeSeconds,
            float radius,
            Vector2 halfExtents,
            float rotationRadians,
            CombatShapeType shapeType,
            DamageSnapshot damage,
            bool directDamageEnabled = true,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f)
        {
            Enabled = projectileTypeId >= 0;
            ProjectileTypeId = projectileTypeId;
            TargetMask = targetMask;
            Count = Mathf.Max(1, count);
            SpreadDegrees = Mathf.Max(0f, spreadDegrees);
            Speed = Mathf.Max(0f, speed);
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            Radius = Mathf.Max(0f, radius);
            HalfExtents = halfExtents;
            RotationRadians = rotationRadians;
            ShapeType = shapeType;
            Damage = damage;
            DirectDamageEnabled = directDamageEnabled;
            PierceCount = Mathf.Max(0, pierceCount);
            RepeatHitCooldownSeconds = Mathf.Max(0f, repeatHitCooldownSeconds);
        }

        public bool Enabled { get; }
        public int ProjectileTypeId { get; }
        public int TargetMask { get; }
        public int Count { get; }
        public float SpreadDegrees { get; }
        public float Speed { get; }
        public float LifetimeSeconds { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public float RotationRadians { get; }
        public CombatShapeType ShapeType { get; }
        public DamageSnapshot Damage { get; }
        public bool DirectDamageEnabled { get; }
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; cleared during simulation/replay.
    public struct CombatHitElement : IBufferElementData
    {
        public int SourceId;
        public int TypeId;
        public int TargetId;
        public float2 Position;
        public CombatHitKind Kind;
        public float DamageAmount;
        public float CritChance;
        public float CritMultiplier;
        public bool DirectDamageEnabled;
        public EntityId SourceNodeId;
        public CombatStackEffectSnapshot StackEffect;
        public ProjectileImpactAoeSnapshot ImpactAoe;
        public ProjectileImpactProjectileSnapshot ImpactProjectile;
        public AoeProjectileBurstSnapshot ProjectileBurst;
        public uint Order;
    }

    // ECS Lifecycle: transient native payload; not added to entities; queued during collision hit flush.
    public struct CombatPendingHit
    {
        public Entity Scope;
        public int SourceId;
        public int TypeId;
        public int TargetId;
        public float2 Position;
        public CombatHitKind Kind;
        public float DamageAmount;
        public float CritChance;
        public float CritMultiplier;
        public bool DirectDamageEnabled;
        public EntityId SourceNodeId;
        public CombatStackEffectSnapshot StackEffect;
        public ProjectileImpactAoeSnapshot ImpactAoe;
        public ProjectileImpactProjectileSnapshot ImpactProjectile;
        public AoeProjectileBurstSnapshot ProjectileBurst;
        public uint Order;
    }
}
