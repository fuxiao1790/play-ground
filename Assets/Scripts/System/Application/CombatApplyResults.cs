using PlayGround.System.Combat.Targets;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Application
{
    public struct CombatSpawnResult
    {
        // the even owner that created the spawn intent
        public Entity Source;
    }

    // ECS Lifecycle: singleton spawn result lane; created and disposed by SpawnIntakeSystem,
    // written during simulation, and drained by CombatSpawnResultBridge during presentation.
    public struct CombatSpawnResultSingleton : IComponentData
    {
        public NativeList<CombatSpawnResult> Results;
        public JobHandle ProducerHandle;
    }

    // ECS Lifecycle: singleton compact combat result lane; created by
    // CombatApplyFinalizeSingleSystem, written during simulation, drained by
    // CombatApplyBridge during presentation, disposed with the apply system.
    public struct CombatApplyResultSingleton : IComponentData
    {
        public NativeList<CombatTickResult> Results;
        public NativeList<StatusStackSnapshot> StatusSnapshots;
        public NativeReference<int> DropCount;
        public JobHandle ProducerHandle;

        public void Clear()
        {
            if (Results.IsCreated)
            {
                Results.Clear();
            }

            if (StatusSnapshots.IsCreated)
            {
                StatusSnapshots.Clear();
            }

            if (DropCount.IsCreated)
            {
                DropCount.Value = 0;
            }
        }

        public void EnsureCapacity(int resultCapacity, int statusSnapshotCapacity)
        {
            if (Results.IsCreated && Results.Capacity < resultCapacity)
            {
                Results.Capacity = resultCapacity;
            }

            if (StatusSnapshots.IsCreated && StatusSnapshots.Capacity < statusSnapshotCapacity)
            {
                StatusSnapshots.Capacity = statusSnapshotCapacity;
            }
        }
    }

    public struct CombatTickResult
    {
        public Entity TargetProxy;
        public float Health;

        // Direct-damage-only aggregate: accrued only when the accepted hit's
        // CombatHitPayload.DirectDamageEnabled is true.
        public float DamageTaken;

        // Accepted CombatHitEvent count for this target during this finalizer
        // update, including non-damaging/status-only hits.
        public int HitCount;

        // Direct-damage-only aggregate: accrued only when the accepted hit's
        // CombatHitPayload.DirectDamageEnabled is true.
        public int CritCount;
        public int StatusStart;
        public int StatusCount;

        // The source ECS finalizer's SystemAPI.Time.DeltaTime for the update
        // that produced this result.
        public float TickDeltaSeconds;
    }
}
