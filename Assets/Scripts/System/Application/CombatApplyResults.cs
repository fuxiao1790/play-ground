using PlayGround.System.Combat.Targets;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Application
{
    // ECS Lifecycle: singleton compact combat result lane; created by
    // CombatApplyFinalizeSingleSystem, written during simulation, drained by
    // CombatApplyBridge during presentation, disposed with the apply system.
    public struct CombatApplyResultSingleton : IComponentData
    {
        public NativeList<CombatTickResult> Results;
        public NativeList<StatusStackSnapshot> StatusSnapshots;
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
        public float DamageTaken;
        public int HitCount;
        public int CritCount;
        public int StatusStart;
        public int StatusCount;
    }
}
