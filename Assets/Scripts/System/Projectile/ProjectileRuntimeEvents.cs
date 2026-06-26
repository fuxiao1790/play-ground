using PlayGround.Common;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public readonly struct ProjectileHitPayload
    {
        public ProjectileHitPayload(
            CombatHitPayload hitPayload,
            OnHitSpawnRef onHitSpawn = default)
        {
            HitPayload = hitPayload;
            OnHitSpawn = onHitSpawn;
        }

        public CombatHitPayload HitPayload { get; }
        public OnHitSpawnRef OnHitSpawn { get; }

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
