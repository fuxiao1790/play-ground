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

namespace PlayGround.System.Combat.Projectiles
{
    public readonly struct ProjectileHitPayload
    {
        public ProjectileHitPayload(CombatHitPayload hitPayload)
        {
            HitPayload = hitPayload;
        }

        public CombatHitPayload HitPayload { get; }

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
