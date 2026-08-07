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
using Unity.Collections;
using Unity.Jobs;
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
    //   snapshot and writes it to CombatStatsSingleton; it also fills ActiveProjectiles/ActiveAoes
    //   from its own render-active queries. It then mirrors the full result to
    //   CombatStatsDisplaySingleton (below), which is the only component game-object code (e.g.
    //   the debug overlay) may read — it is never reset, so readers never see a zeroed or
    //   partial mid-frame value. The simulation holds no reference to any Debugging type.
    //
    // Field producers:
    // - EntitiesSpawnedViaEcb: top-up create total, summed (+=) by every domain's spawn-apply
    //   systems from their per-frame pool-growth counts.
    // - EntitiesSpawnedViaReuse: reuse total, summed (+=) by those same spawn-apply systems from
    //   the disabled Active slots they reclaimed.
    // - TargetedEntitiesSpawned: targeted-only spawn total, summed by both targeted apply lanes.
    // - TargetedLinksResolved: targeted-only links, drained by gather from TargetedLinkCounts.
    // - ActiveProjectiles / ActiveAoes / ActiveTargeted: written by CombatStatsGatherSystem from
    //   its Active entity queries per domain tag. Preserved by the frame reset (level stats, not
    //   accumulators); CombatPoolCleanupSystem's calm-down gate reads them one frame stale.
    // - HitEventsCreated: added by CombatApplyFinalizeSingleSystem from HitQueue.Count before the
    //   queue is flattened or cleared.
    // - VfxEventsCreated: added by CombatAoeVfxDispatchSystem after the VFX root drains PendingCircularSpawns.
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
        public int TargetedEntitiesSpawned;
        public int TargetedLinksResolved;
        public int ActiveProjectiles;
        public int ActiveAoes;
        public int ActiveTargeted;
        public int HitEventsCreated;
        public int VfxEventsCreated;
        public int EntitiesDespawned;
        public int EntitiesDeleted;

        // ECS Lifecycle: stats-owned per-frame link contributions; created/disposed by
        // CombatStatsGatherSystem and drained into TargetedLinksResolved once per frame.
        public NativeQueue<int> TargetedLinkCounts;
        public JobHandle TargetedLinkProducerHandle;
    }

    // ECS Lifecycle: singleton data; the GameObject-facing mirror of CombatStatsSingleton.
    // Lives on the same stats entity for the world's lifetime. CombatStatsGatherSystem is the
    // only writer, publishing a full copy once per frame after every producer (and the frame
    // reset) has run. Nothing ever resets or partially writes this component, so game-object
    // readers (e.g. PerformanceText) always see a complete, stable snapshot instead of racing
    // CombatStatsResetSystem's mid-frame zeroing of the internal accumulator.
    public struct CombatStatsDisplaySingleton : Unity.Entities.IComponentData
    {
        public int EntitiesSpawnedViaEcb;
        public int EntitiesSpawnedViaReuse;
        public int TargetedEntitiesSpawned;
        public int TargetedLinksResolved;
        public int ActiveProjectiles;
        public int ActiveAoes;
        public int ActiveTargeted;
        public int HitEventsCreated;
        public int VfxEventsCreated;
        public int EntitiesDespawned;
        public int EntitiesDeleted;
    }
}
