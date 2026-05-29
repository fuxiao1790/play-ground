using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public readonly struct ProjectileHitContext
    {
        public ProjectileHitContext(
            int projectileId,
            int projectileTypeId,
            int targetId,
            Vector2 position,
            DamageSnapshot damage,
            IProjectileTarget target = null)
        {
            ProjectileId = projectileId;
            ProjectileTypeId = projectileTypeId;
            TargetId = targetId;
            Position = position;
            Damage = damage;
            Target = target;
        }

        public int ProjectileId { get; }
        public int ProjectileTypeId { get; }
        public int TargetId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
        public IProjectileTarget Target { get; }
    }

    public readonly struct ProjectileChildSpawnRequest
    {
        public ProjectileChildSpawnRequest(
            int projectileId,
            int projectileTypeId,
            int childSpawnerId,
            int tickIndex,
            int childProjectileId,
            Vector2 position,
            Vector2 velocity,
            DamageSnapshot damage)
        {
            ProjectileId = projectileId;
            ProjectileTypeId = projectileTypeId;
            ChildSpawnerId = childSpawnerId;
            TickIndex = tickIndex;
            ChildProjectileId = childProjectileId;
            Position = position;
            Velocity = velocity;
            Damage = damage;
        }

        public int ProjectileId { get; }
        public int ProjectileTypeId { get; }
        public int ChildSpawnerId { get; }
        public int TickIndex { get; }
        public int ChildProjectileId { get; }
        public Vector2 Position { get; }
        public Vector2 Velocity { get; }
        public DamageSnapshot Damage { get; }
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
