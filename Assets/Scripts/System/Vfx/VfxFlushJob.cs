using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Vfx
{
    [BurstCompile]
    public struct VfxFlushJob : IJob
    {
        public NativeQueue<VfxPendingSpawn> Pending;
        public BufferLookup<VfxSpawnRequestElement> VfxBuffers;

        public void Execute()
        {
            while (Pending.TryDequeue(out VfxPendingSpawn p))
            {
                if (p.Scope == Entity.Null || !VfxBuffers.HasBuffer(p.Scope))
                {
                    continue;
                }

                VfxBuffers[p.Scope].Add(new VfxSpawnRequestElement
                {
                    TypeId = p.TypeId,
                    Trigger = p.Trigger,
                    Position = p.Position
                });
            }
        }
    }
}
