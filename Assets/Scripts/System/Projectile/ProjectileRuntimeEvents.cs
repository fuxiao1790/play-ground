using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public readonly struct ProjectileHitPayload
    {
        public ProjectileHitPayload(EntityId sourceNodeId, float damageAmount, bool directDamageEnabled)
        {
            SourceNodeId = sourceNodeId;
            DamageAmount = damageAmount;
            DirectDamageEnabled = directDamageEnabled;
        }

        public EntityId SourceNodeId { get; }
        public float DamageAmount { get; }
        public bool DirectDamageEnabled { get; }
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
