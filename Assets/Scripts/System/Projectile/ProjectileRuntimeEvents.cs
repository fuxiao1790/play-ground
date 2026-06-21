using PlayGround.Common;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public readonly struct ProjectileHitPayload
    {
        public ProjectileHitPayload(
            CombatHitPayload hitPayload,
            ProjectileImpactAoeSnapshot impactAoe = default,
            ProjectileImpactProjectileSnapshot impactProjectile = default)
        {
            HitPayload = hitPayload;
            ImpactAoe = impactAoe;
            ImpactProjectile = impactProjectile;
        }

        public CombatHitPayload HitPayload { get; }
        public ProjectileImpactAoeSnapshot ImpactAoe { get; }
        public ProjectileImpactProjectileSnapshot ImpactProjectile { get; }

        public float DamageAmount => HitPayload.DamageAmount;
        public float CritChance => HitPayload.CritChance;
        public float CritMultiplier => HitPayload.CritMultiplier;
        public bool DirectDamageEnabled => HitPayload.DirectDamageEnabled;
        public EntityId SourceNodeId => HitPayload.SourceNodeId;
        public StackEffectSnapshot StackEffect => HitPayload.StackEffect;
        public DamageSnapshot Damage => new(HitPayload.DamageAmount);
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
