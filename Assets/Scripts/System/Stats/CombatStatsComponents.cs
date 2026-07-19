using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
namespace PlayGround.System.Combat.Stats
{
    // ECS Lifecycle: singleton data; the shared per-frame stats blackboard. The entity is
    // created once by CombatStatsGatherSystem.OnCreate and never removed (dies with the world).
    //
    // Data flow (nobody reaches into another system for these values):
    // - CombatStatsResetSystem (InitializationSystemGroup) zeroes the per-frame accumulators at
    //   the start of every frame. ActiveProjectiles/ActiveAoes are level stats and survive the
    //   reset; mid-frame readers see last frame's values until gather overwrites them.
    // - Each producing system accumulates its own contribution into the relevant field via
    //   SystemAPI.TryGetSingletonRW during simulation/presentation, so idle frames add nothing
    //   and never show stale counts.
    // - CombatStatsGatherSystem (PresentationSystemGroup, after all producers) reads the built-up
    //   snapshot and pushes it to the overlay; it also fills ActiveProjectiles/ActiveAoes from
    //   its own render-active queries.
    //
    // Field producers:
    // - EntitiesSpawnedViaEcb: top-up create total, summed (+=) by the projectile, impact AOE,
    //   and lingering AOE spawn-apply systems from their per-frame pool-growth counts.
    // - EntitiesSpawnedViaReuse: reuse total, summed (+=) by those same spawn-apply systems from
    //   the disabled Active slots they reclaimed.
    // - ActiveProjectiles / ActiveAoes: written by CombatStatsGatherSystem from its Active
    //   entity queries per domain tag. Preserved by the frame reset (level stats, not
    //   accumulators); CombatPoolCleanupSystem's calm-down gate reads them one frame stale.
    // - HitEventsCreated: added by CombatApplyFinalizeSingleSystem from HitQueue.Count before the
    //   queue is flattened or cleared.
    // - VfxEventsCreated: added by CombatAoeVfxDispatchSystem after the VFX root drains PendingBasicSpawns.
    //   Counts only requests accepted by CombatAoeVfxDispatcher.StageAoeSpawn (a VFX resource registered
    //   for (typeId, trigger), still under its max-per-frame cap); blindly queued requests with no
    //   registered visual do not contribute.
    // - EntitiesDespawned: added by CombatPoolCleanupSystem; no expiry/collision system counts
    //   despawns directly. Derived by conservation from the spawn counters and the change in the
    //   gathered active counts (despawns = spawns - delta active), so the value runs one frame
    //   behind the other counters. Feeds the cleanup calm-down gate and the overlay.
    // - EntitiesDeleted: added by CombatPoolCleanupSystem from the pool entities its trimmer
    //   destroyed this frame.
    public struct CombatStatsSingleton : Unity.Entities.IComponentData
    {
        public int EntitiesSpawnedViaEcb;
        public int EntitiesSpawnedViaReuse;
        public int ActiveProjectiles;
        public int ActiveAoes;
        public int HitEventsCreated;
        public int VfxEventsCreated;
        public int EntitiesDespawned;
        public int EntitiesDeleted;
    }

    // ECS Lifecycle: managed singleton binding; created with the singleton entity;
    // Display is set/cleared by PerformanceText via CombatStatsGatherSystem.Bind/Unbind.
    public sealed class CombatStatsBinding : Unity.Entities.IComponentData
    {
        public global::PerformanceText Display;
    }
}
