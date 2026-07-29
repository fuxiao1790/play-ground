using Unity.Collections;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Vfx
{
    public static class VfxEmit
    {
        public static void EnqueueLineSegment(
            int vfxId,
            float2 startPosition,
            float2 endPosition,
            float width,
            in NativeQueue<LineSegmentVfxSpawn>.ParallelWriter lineSegments)
        {
            if (vfxId <= 0 || VfxDataShapeTable.DecodeShape(vfxId) != VfxDataShape.LineSegment)
            {
                return;
            }

            lineSegments.Enqueue(new LineSegmentVfxSpawn
            {
                VfxId = vfxId,
                StartPosition = startPosition,
                EndPosition = endPosition,
                Width = width
            });
        }

        public static void Enqueue(
            int vfxId,
            float2 position,
            float areaSize,
            in VfxTimingData timing,
            in NativeQueue<CircularVfxSpawnRequest>.ParallelWriter circular,
            in NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter timedCircular)
        {
            if (vfxId <= 0)
            {
                return;
            }

            switch (VfxDataShapeTable.DecodeShape(vfxId))
            {
                case VfxDataShape.Circular:
                    circular.Enqueue(new CircularVfxSpawnRequest
                    {
                        VfxId = vfxId,
                        Position = position,
                        AreaSize = areaSize
                    });
                    break;
                case VfxDataShape.TimedCircular:
                    timedCircular.Enqueue(new TimedCircularVfxSpawnRequest
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
