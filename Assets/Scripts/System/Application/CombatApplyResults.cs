using PlayGround.System.Combat.Targets;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Combat.Application
{
    public struct CombatSpawnResult
    {
        // Mana adds outcome, assigned-id, and cost fields later.
        public Entity Caster;
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
        public float DamageTaken;
        public int HitCount;
        public int CritCount;
        public int StatusStart;
        public int StatusCount;
    }
}
