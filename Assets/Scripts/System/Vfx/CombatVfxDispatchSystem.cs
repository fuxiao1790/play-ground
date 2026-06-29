using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Vfx
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatVfxDispatchSystem : SystemBase
    {
        internal NativeQueue<VfxPendingSpawn> PendingSpawns;
        internal JobHandle ProducerHandle;
        internal bool HasQueue => PendingSpawns.IsCreated;
        internal int LastVfxEventCount;

        internal NativeQueue<VfxPendingSpawn>.ParallelWriter AsParallelWriter() =>
            PendingSpawns.AsParallelWriter();

        protected override void OnCreate()
        {
            PendingSpawns = new NativeQueue<VfxPendingSpawn>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            ProducerHandle.Complete();
            if (PendingSpawns.IsCreated)
            {
                PendingSpawns.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            ProducerHandle.Complete();
            ProducerHandle = default;
            LastVfxEventCount = 0;

            if (PendingSpawns.Count == 0)
            {
                return;
            }

            CombatVfxRoot root = CombatVfxRoot.Instance;
            if (root == null)
            {
                PendingSpawns.Clear();
                return;
            }

            LastVfxEventCount = root.DrainAndDispatch(ref PendingSpawns);
        }
    }
}
