using PlayGround.Common;
using PlayGround.System.Aoe;
using Unity.Entities;
using Unity.Mathematics;
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

    public readonly struct ProjectileImpactAoeSnapshot
    {
        public ProjectileImpactAoeSnapshot(
            int typeId,
            int targetMask,
            float damageAmount,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            AoeSpawnGeometry geometry = default,
            float critChance = 0f,
            float critMultiplier = 1.5f,
            float areaSize = 1f,
            StackEffectSnapshot stackEffect = default,
            AoeOnHitSpawnSnapshot aoeSpawn = default)
        {
            Enabled = typeId >= 0;
            TypeId = typeId;
            TargetMask = targetMask;
            DamageAmount = damageAmount;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            Geometry = geometry;
            AreaSize = geometry.IsValid ? geometry.AreaSize : Mathf.Max(0.01f, areaSize);
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            StackEffect = stackEffect;
            AoeSpawn = aoeSpawn;
        }

        public int TypeId { get; }
        public int TargetMask { get; }
        public float DamageAmount { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeSpawnGeometry Geometry { get; }
        public float AreaSize { get; }
        public bool Enabled { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public StackEffectSnapshot StackEffect { get; }
        public AoeOnHitSpawnSnapshot AoeSpawn { get; }
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
            StackEffectSnapshot stackEffect = default,
            float visualScale = 0f,
            float visualRotationDegrees = 0f)
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
            VisualScale = Mathf.Max(0f, visualScale);
            VisualRotationDegrees = visualRotationDegrees;
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
        public StackEffectSnapshot StackEffect { get; }
        public float VisualScale { get; }
        public float VisualRotationDegrees { get; }
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
            float repeatHitCooldownSeconds = 0f,
            ProjectileTrackingConfig tracking = default,
            StackEffectSnapshot stackEffect = default,
            float visualScale = 0f,
            float visualRotationDegrees = 0f)
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
            StackEffect = stackEffect;
            VisualScale = Mathf.Max(0f, visualScale);
            VisualRotationDegrees = visualRotationDegrees;
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
        public StackEffectSnapshot StackEffect { get; }
        public float VisualScale { get; }
        public float VisualRotationDegrees { get; }
    }

    public readonly struct AoeOnHitSpawnTailSnapshot
    {
        public AoeOnHitSpawnTailSnapshot(
            int typeId,
            int targetMask,
            float damageAmount,
            bool directDamageEnabled,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            AoeSpawnGeometry geometry,
            float critChance,
            float critMultiplier,
            int stackDebuffKey = -1,
            int stackThreshold = 0,
            float stackLifetime = 0f,
            StackContribution stackContribution = default,
            StackDetonationKind stackDetonationKind = StackDetonationKind.None,
            int stackDetonationTypeId = -1,
            float stackDetonationLifetimeSeconds = 0f,
            float stackDetonationTickIntervalSeconds = 0f,
            AoeSpawnGeometry stackDetonationAoeGeometry = default,
            float stackDetonationCritChance = 0f,
            float stackDetonationCritMultiplier = 1.5f)
        {
            Enabled = typeId >= 0 && geometry.IsValid;
            TypeId = typeId;
            TargetMask = targetMask;
            DamageAmount = Mathf.Max(0f, damageAmount);
            DirectDamageEnabled = directDamageEnabled;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            Geometry = geometry;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            StackDebuffKey = stackDebuffKey;
            StackThreshold = Mathf.Max(0, stackThreshold);
            StackLifetime = Mathf.Max(0f, stackLifetime);
            StackContribution = stackContribution;
            StackDetonationKind = stackDetonationKind;
            StackDetonationTypeId = stackDetonationTypeId;
            StackDetonationLifetimeSeconds = Mathf.Max(0f, stackDetonationLifetimeSeconds);
            StackDetonationTickIntervalSeconds = Mathf.Max(0f, stackDetonationTickIntervalSeconds);
            StackDetonationAoeGeometry = stackDetonationAoeGeometry;
            StackDetonationCritChance = stackDetonationCritChance;
            StackDetonationCritMultiplier = stackDetonationCritMultiplier;
        }

        public bool Enabled { get; }
        public int TypeId { get; }
        public int TargetMask { get; }
        public float DamageAmount { get; }
        public bool DirectDamageEnabled { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeSpawnGeometry Geometry { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public int StackDebuffKey { get; }
        public int StackThreshold { get; }
        public float StackLifetime { get; }
        public StackContribution StackContribution { get; }
        public StackDetonationKind StackDetonationKind { get; }
        public int StackDetonationTypeId { get; }
        public float StackDetonationLifetimeSeconds { get; }
        public float StackDetonationTickIntervalSeconds { get; }
        public AoeSpawnGeometry StackDetonationAoeGeometry { get; }
        public float StackDetonationCritChance { get; }
        public float StackDetonationCritMultiplier { get; }

        public AoeOnHitSpawnSnapshot ToSnapshot() => Enabled
            ? new AoeOnHitSpawnSnapshot(
                TypeId,
                TargetMask,
                DamageAmount,
                DirectDamageEnabled,
                LifetimeSeconds,
                TickIntervalSeconds,
                Geometry,
                CritChance,
                CritMultiplier,
                StackDebuffKey,
                StackThreshold,
                StackLifetime,
                StackContribution,
                StackDetonationKind,
                StackDetonationTypeId,
                StackDetonationLifetimeSeconds,
                StackDetonationTickIntervalSeconds,
                StackDetonationAoeGeometry,
                StackDetonationCritChance,
                StackDetonationCritMultiplier)
            : default;
    }

    public readonly struct AoeOnHitSpawnSnapshot
    {
        public const int MaxStackChainLinks = 2;

        public AoeOnHitSpawnSnapshot(
            int typeId,
            int targetMask,
            float damageAmount,
            bool directDamageEnabled,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            AoeSpawnGeometry geometry,
            float critChance,
            float critMultiplier,
            int stackDebuffKey = -1,
            int stackThreshold = 0,
            float stackLifetime = 0f,
            StackContribution stackContribution = default,
            StackDetonationKind stackDetonationKind = StackDetonationKind.None,
            int stackDetonationTypeId = -1,
            float stackDetonationLifetimeSeconds = 0f,
            float stackDetonationTickIntervalSeconds = 0f,
            AoeSpawnGeometry stackDetonationAoeGeometry = default,
            float stackDetonationCritChance = 0f,
            float stackDetonationCritMultiplier = 1.5f,
            AoeOnHitSpawnTailSnapshot nextAoeOnHitSpawn = default)
        {
            Enabled = typeId >= 0 && geometry.IsValid;
            TypeId = typeId;
            TargetMask = targetMask;
            DamageAmount = Mathf.Max(0f, damageAmount);
            DirectDamageEnabled = directDamageEnabled;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            Geometry = geometry;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            StackDebuffKey = stackDebuffKey;
            StackThreshold = Mathf.Max(0, stackThreshold);
            StackLifetime = Mathf.Max(0f, stackLifetime);
            StackContribution = stackContribution;
            StackDetonationKind = stackDetonationKind;
            StackDetonationTypeId = stackDetonationTypeId;
            StackDetonationLifetimeSeconds = Mathf.Max(0f, stackDetonationLifetimeSeconds);
            StackDetonationTickIntervalSeconds = Mathf.Max(0f, stackDetonationTickIntervalSeconds);
            StackDetonationAoeGeometry = stackDetonationAoeGeometry;
            StackDetonationCritChance = stackDetonationCritChance;
            StackDetonationCritMultiplier = stackDetonationCritMultiplier;
            NextAoeOnHitSpawn = nextAoeOnHitSpawn;
        }

        public bool Enabled { get; }
        public int TypeId { get; }
        public int TargetMask { get; }
        public float DamageAmount { get; }
        public bool DirectDamageEnabled { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeSpawnGeometry Geometry { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public int StackDebuffKey { get; }
        public int StackThreshold { get; }
        public float StackLifetime { get; }
        public StackContribution StackContribution { get; }
        public StackDetonationKind StackDetonationKind { get; }
        public int StackDetonationTypeId { get; }
        public float StackDetonationLifetimeSeconds { get; }
        public float StackDetonationTickIntervalSeconds { get; }
        public AoeSpawnGeometry StackDetonationAoeGeometry { get; }
        public float StackDetonationCritChance { get; }
        public float StackDetonationCritMultiplier { get; }
        public AoeOnHitSpawnTailSnapshot NextAoeOnHitSpawn { get; }

        public StackEffectSnapshot BuildStackEffect(CombatFaction faction)
        {
            var detonation = new DetonationSnapshot
            {
                Kind = StackDetonationKind,
                Faction = faction,
                TargetMask = TargetMask,
                TypeId = StackDetonationTypeId,
                LifetimeSeconds = StackDetonationLifetimeSeconds,
                TickIntervalSeconds = StackDetonationTickIntervalSeconds,
                AoeGeometry = StackDetonationAoeGeometry,
                CritChance = StackDetonationCritChance,
                CritMultiplier = StackDetonationCritMultiplier,
                AoeOnHitSpawn = NextAoeOnHitSpawn.ToSnapshot()
            };

            return new StackEffectSnapshot
            {
                DebuffKey = StackDebuffKey,
                Threshold = StackThreshold,
                Lifetime = StackLifetime,
                Contribution = StackContribution,
                Detonation = detonation
            };
        }
    }

    // ECS Lifecycle: transient native hit payload; not added to entities; enqueued during collision,
    // bucketed and aggregated by CombatApplyFinalizeSystem, then handed to CombatApplyBridge.
    public struct CombatHitEvent
    {
        public Entity TargetProxy;
        public CombatHitKind Kind;
        public float DamageAmount;
        public float CritChance;
        public float CritMultiplier;
        public bool DirectDamageEnabled;
        public float2 HitPosition;
        public EntityId SourceNodeId;
        public int SourceId;
        public int TypeId;
        public StackEffectSnapshot StackEffect;
    }
}
