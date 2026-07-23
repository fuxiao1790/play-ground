using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Vfx
{
    public static class VfxEmit
    {
        public static void Enqueue(
            int vfxId,
            float2 position,
            float areaSize,
            in VfxTimingData timing,
            in NativeQueue<VfxSpawnRequest>.ParallelWriter basic,
            in NativeQueue<TimedVfxSpawnRequest>.ParallelWriter timed)
        {
            if (vfxId <= 0)
            {
                return;
            }

            switch (VfxDataShapeTable.DecodeShape(vfxId))
            {
                case VfxDataShape.Basic:
                    basic.Enqueue(new VfxSpawnRequest
                    {
                        VfxId = vfxId,
                        Position = position,
                        AreaSize = areaSize
                    });
                    break;
                case VfxDataShape.Timed:
                    timed.Enqueue(new TimedVfxSpawnRequest
                    {
                        VfxId = vfxId,
                        Position = position,
                        AreaSize = areaSize,
                        Duration = timing.Duration,
                        TickInterval = timing.TickInterval
                    });
                    break;
            }
        }
    }
}
