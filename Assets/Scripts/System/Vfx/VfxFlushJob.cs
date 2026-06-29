using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Vfx
{
    [BurstCompile]
    public struct VfxFlushJob : IJob
    {
        public Entity Scope;
        public NativeQueue<VfxPendingSpawn> Pending;
        public BufferLookup<VfxSpawnRequestElement> VfxBuffers;

        public void Execute()
        {
            while (Pending.TryDequeue(out VfxPendingSpawn p))
            {
                if (!VfxBuffers.HasBuffer(Scope))
                {
                    continue;
                }

                VfxBuffers[Scope].Add(new VfxSpawnRequestElement
                {
                    TypeId = p.TypeId,
                    Trigger = p.Trigger,
                    Position = p.Position,
                    AreaSize = p.AreaSize > 0f ? p.AreaSize : 1f
                });
            }
        }
    }

    [BurstCompile]
    public struct VfxStreamFlushJob : IJob
    {
        public Entity Scope;
        public NativeStream Pending;
        public BufferLookup<VfxSpawnRequestElement> VfxBuffers;

        public void Execute()
        {
            NativeStream.Reader reader = Pending.AsReader();
            for (int i = 0; i < reader.ForEachCount; i++)
            {
                int count = reader.BeginForEachIndex(i);
                for (int j = 0; j < count; j++)
                {
                    Write(reader.Read<VfxPendingSpawn>());
                }

                reader.EndForEachIndex();
            }
        }

        private void Write(VfxPendingSpawn p)
        {
            if (!VfxBuffers.HasBuffer(Scope))
            {
                return;
            }

            VfxBuffers[Scope].Add(new VfxSpawnRequestElement
            {
                TypeId = p.TypeId,
                Trigger = p.Trigger,
                Position = p.Position,
                AreaSize = p.AreaSize > 0f ? p.AreaSize : 1f
            });
        }
    }
}
