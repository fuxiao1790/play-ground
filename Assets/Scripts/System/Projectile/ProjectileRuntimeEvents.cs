using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public readonly struct ProjectileStackEffectSnapshot
    {
        public ProjectileStackEffectSnapshot(
            int debuffStatusId,
            int stacksPerHit,
            int stackThreshold,
            int aoeTypeId,
            float aoeDamage,
            float aoeLifetimeSeconds,
            float aoeTickIntervalSeconds)
        {
            Enabled = aoeTypeId >= 0;
            DebuffStatusId = debuffStatusId;
            StacksPerHit = stacksPerHit;
            StackThreshold = stackThreshold;
            AoeTypeId = aoeTypeId;
            AoeDamage = aoeDamage;
            AoeLifetimeSeconds = Mathf.Max(0f, aoeLifetimeSeconds);
            AoeTickIntervalSeconds = Mathf.Max(0f, aoeTickIntervalSeconds);
        }

        public bool Enabled { get; }
        public int DebuffStatusId { get; }
        public int StacksPerHit { get; }
        public int StackThreshold { get; }
        public int AoeTypeId { get; }
        public float AoeDamage { get; }
        public float AoeLifetimeSeconds { get; }
        public float AoeTickIntervalSeconds { get; }
    }

    public readonly struct ProjectileImpactAoeSnapshot
    {
        public ProjectileImpactAoeSnapshot(
            int typeId,
            int targetMask,
            float damageAmount,
            float lifetimeSeconds,
            float tickIntervalSeconds)
        {
            Enabled = typeId >= 0;
            TypeId = typeId;
            TargetMask = targetMask;
            DamageAmount = damageAmount;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
        }

        public int TypeId { get; }
        public int TargetMask { get; }
        public float DamageAmount { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public bool Enabled { get; }
        public DamageSnapshot Damage => new(Mathf.Max(0f, DamageAmount));
    }

    public readonly struct ProjectileHitPayload
    {
        public ProjectileHitPayload(
            EntityId sourceNodeId,
            float damageAmount,
            bool directDamageEnabled,
            ProjectileImpactAoeSnapshot impactAoe = default,
            ProjectileStackEffectSnapshot stackEffect = default,
            float critChance = 0f,
            float critMultiplier = 1.5f)
        {
            SourceNodeId = sourceNodeId;
            DamageAmount = damageAmount;
            DirectDamageEnabled = directDamageEnabled;
            ImpactAoe = impactAoe;
            StackEffect = stackEffect;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
        }

        public EntityId SourceNodeId { get; }
        public float DamageAmount { get; }
        public bool DirectDamageEnabled { get; }
        public ProjectileImpactAoeSnapshot ImpactAoe { get; }
        public ProjectileStackEffectSnapshot StackEffect { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public DamageSnapshot Damage => new(DamageAmount);
    }

    public readonly struct ProjectileHitContext
    {
        public ProjectileHitContext(
            int projectileId,
            int projectileTypeId,
            int targetId,
            Vector2 position,
            DamageSnapshot damage,
            ProjectileHitPayload payload,
            IProjectileTarget target = null)
        {
            ProjectileId = projectileId;
            ProjectileTypeId = projectileTypeId;
            TargetId = targetId;
            Position = position;
            Damage = damage;
            Payload = payload;
            Target = target;
        }

        public int ProjectileId { get; }
        public int ProjectileTypeId { get; }
        public int TargetId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
        public ProjectileHitPayload Payload { get; }
        public IProjectileTarget Target { get; }
    }

    public readonly struct ProjectileRuntimeCounters
    {
        public ProjectileRuntimeCounters(
            int activeProjectiles,
            int spawnedProjectiles,
            int despawnedProjectiles,
            int hitEvents,
            int childSpawnRequests)
        {
            ActiveProjectiles = activeProjectiles;
            SpawnedProjectiles = spawnedProjectiles;
            DespawnedProjectiles = despawnedProjectiles;
            HitEvents = hitEvents;
            ChildSpawnRequests = childSpawnRequests;
        }

        public int ActiveProjectiles { get; }
        public int SpawnedProjectiles { get; }
        public int DespawnedProjectiles { get; }
        public int HitEvents { get; }
        public int ChildSpawnRequests { get; }
    }
}
