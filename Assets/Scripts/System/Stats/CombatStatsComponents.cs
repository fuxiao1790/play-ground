namespace PlayGround.System.Stats
{
    // ECS Lifecycle: singleton data; created once by CombatStatsGatherSystem.OnCreate;
    // overwritten each frame by that system; never removed (dies with the world).
    //
    // Stat tracking:
    // - EntitiesSpawnedViaEcb sums LastColdCreateCount from basic projectile,
    //   child-spawner projectile, and AOE spawn apply systems. Each producer sets
    //   that value from the same per-frame total used by its profiler cold-create
    //   counter, after disabled-slot reuse has been counted.
    // - EntitiesSpawnedViaReuse sums LastReuseCount from those same spawn apply
    //   systems. Each producer sets it from the claimed disabled Active slots
    //   counted during its reuse job phase.
    // - ActiveProjectiles and ActiveAoes mirror CombatBatchedRenderSystem's
    //   last render-active counts. The render system already materializes
    //   per-batch NativeArrays for drawing, so it records their Lengths while
    //   doing that existing work; gathering only reads cached ints.
    // - HitEventsCreated mirrors CombatApplyFinalizeSystem.LastHitEventCount,
    //   assigned from HitQueue.Count before the queue is flattened or cleared.
    // - VfxEventsCreated mirrors CombatVfxDispatchSystem.LastVfxEventCount,
    //   assigned after the VFX root drains PendingSpawns. It counts only requests
    //   accepted by CombatVfxDispatcher.StageSpawn: a VFX resource must be
    //   registered for (typeId, trigger), and that resource must still be under
    //   its max-per-frame cap. Blindly queued collision/lifetime/pulse requests
    //   with no registered visual do not contribute.
    // Producers write zero on no-work paths, so idle frames do not show stale
    // counts. This singleton is just the presentation snapshot; producers remain
    // the source of each per-frame value. CombatStatsGatherSystem only reads
    // cached producer int fields; it does not scan queues, entities, or VFX
    // resources.
    public struct CombatStatsSingleton : Unity.Entities.IComponentData
    {
        public int EntitiesSpawnedViaEcb;
        public int EntitiesSpawnedViaReuse;
        public int ActiveProjectiles;
        public int ActiveAoes;
        public int HitEventsCreated;
        public int VfxEventsCreated;
    }

    // ECS Lifecycle: managed singleton binding; created with the singleton entity;
    // Display is set/cleared by PerformanceText via CombatStatsGatherSystem.Bind/Unbind.
    public sealed class CombatStatsBinding : Unity.Entities.IComponentData
    {
        public global::PerformanceText Display;
    }
}
