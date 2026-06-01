using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    public struct ProjectileRecycleFlushJob : IJob
    {
        public NativeQueue<ProjectilePendingRecycle> Recycled;
        public BufferLookup<ProjectileRecycleElement> RecycleBuffers;

        public void Execute()
        {
            while (Recycled.TryDequeue(out ProjectilePendingRecycle recycle))
            {
                if (recycle.Scope == Entity.Null || !RecycleBuffers.HasBuffer(recycle.Scope))
                {
                    continue;
                }

                RecycleBuffers[recycle.Scope].Add(new ProjectileRecycleElement
                {
                    ProjectileEntity = recycle.ProjectileEntity,
                    TypeId = recycle.TypeId,
                    HasChildSpawner = recycle.HasChildSpawner
                });
            }
        }
    }
}
