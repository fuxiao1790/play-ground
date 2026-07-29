using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Combat.Vfx
{
    public enum VfxDataShape : byte
    {
        // Spawn payload: Position and AreaSize.
        Circular = 0,

        // Spawn payload: Position, AreaSize, Duration, and TickInterval.
        TimedCircular = 1,

        // Spawn payload: StartPosition, EndPosition, and Width.
        LineSegment = 2
    }

    public struct CircularVfxSpawnRequest
    {
        public int VfxId;
        public float2 Position;
        public float AreaSize;
    }

    public struct TimedCircularVfxSpawnRequest
    {
        public int VfxId;
        public float2 Position;
        public float AreaSize;
        public float Duration;
        public float TickInterval;
    }

    public struct LineSegmentVfxSpawn
    {
        public int VfxId;
        public float2 StartPosition;
        public float2 EndPosition;
        public float Width;
    }

    public readonly struct VfxDataShapeBuffer
    {
        public VfxDataShapeBuffer(string name, global::System.Type type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }
        public global::System.Type Type { get; }
    }

    public static class VfxDataShapeTable
    {
        public const string PositionsPropertyName = "Positions";
        public const string AreaSizesPropertyName = "AreaSizes";
        public const string DurationsPropertyName = "Durations";
        public const string TickIntervalsPropertyName = "TickIntervals";
        public const string StartPositionsPropertyName = "StartPositions";
        public const string EndPositionsPropertyName = "EndPositions";
        public const string WidthsPropertyName = "Widths";
        public const string SpawnCountPropertyName = "SpawnCount";
        public const string SpawnEventName = "OnSpawn";

        public const int ShapeShift = 27;
        public const int LocalMask = (1 << ShapeShift) - 1;
        private const int ShapeMask = 0x7;

        private static readonly VfxDataShapeBuffer[] CircularBuffers =
        {
            new(PositionsPropertyName, typeof(GraphicsBuffer)),
            new(AreaSizesPropertyName, typeof(GraphicsBuffer))
        };

        private static readonly VfxDataShapeBuffer[] TimedCircularBuffers =
        {
            new(PositionsPropertyName, typeof(GraphicsBuffer)),
            new(AreaSizesPropertyName, typeof(GraphicsBuffer)),
            new(DurationsPropertyName, typeof(GraphicsBuffer)),
            new(TickIntervalsPropertyName, typeof(GraphicsBuffer))
        };

        private static readonly VfxDataShapeBuffer[] LineSegmentBuffers =
        {
            new(StartPositionsPropertyName, typeof(GraphicsBuffer)),
            new(EndPositionsPropertyName, typeof(GraphicsBuffer)),
            new(WidthsPropertyName, typeof(GraphicsBuffer))
        };

        public static IReadOnlyList<VfxDataShapeBuffer> BuffersFor(VfxDataShape shape) =>
            shape switch
            {
                VfxDataShape.Circular => CircularBuffers,
                VfxDataShape.TimedCircular => TimedCircularBuffers,
                VfxDataShape.LineSegment => LineSegmentBuffers,
                _ => CircularBuffers
            };

        public static IEnumerable<string> BufferNamesFor(VfxDataShape shape)
        {
            IReadOnlyList<VfxDataShapeBuffer> buffers = BuffersFor(shape);
            for (int i = 0; i < buffers.Count; i++)
            {
                yield return buffers[i].Name;
            }
        }

        public static int BufferCountFor(VfxDataShape shape) => BuffersFor(shape).Count;

        public static int EncodeId(VfxDataShape shape, int localIndex1Based)
        {
            if (localIndex1Based < 1 || localIndex1Based > LocalMask)
            {
                throw new global::System.ArgumentOutOfRangeException(
                    nameof(localIndex1Based),
                    $"VFX local index must be in [1, {LocalMask}].");
            }

            int shapeOrdinal = (int)shape;
            if (shapeOrdinal < 0 || shapeOrdinal > ShapeMask)
            {
                throw new global::System.ArgumentOutOfRangeException(
                    nameof(shape),
                    "VFX shape ordinal must fit in the encoded id shape bits.");
            }

            return (shapeOrdinal << ShapeShift) | localIndex1Based;
        }

        public static VfxDataShape DecodeShape(int id) =>
            (VfxDataShape)((id >> ShapeShift) & ShapeMask);

        public static int DecodeLocalIndex(int id) => id & LocalMask;
    }
}
