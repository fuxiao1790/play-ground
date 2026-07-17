using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Combat.Vfx
{
    public enum VfxDataShape : byte
    {
        Basic = 0,
        Timed = 1
    }

    public struct VfxSpawnRequest
    {
        public int VfxId;
        public float2 Position;
        public float AreaSize;
    }

    public struct TimedVfxSpawnRequest
    {
        public int VfxId;
        public float2 Position;
        public float AreaSize;
        public float Duration;
        public float TickInterval;
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
        public const string SpawnCountPropertyName = "SpawnCount";
        public const string SpawnEventName = "OnSpawn";

        public const int ShapeShift = 27;
        public const int LocalMask = (1 << ShapeShift) - 1;
        private const int ShapeMask = 0x7;

        private static readonly VfxDataShapeBuffer[] BasicBuffers =
        {
            new(PositionsPropertyName, typeof(GraphicsBuffer)),
            new(AreaSizesPropertyName, typeof(GraphicsBuffer))
        };

        private static readonly VfxDataShapeBuffer[] TimedBuffers =
        {
            new(PositionsPropertyName, typeof(GraphicsBuffer)),
            new(AreaSizesPropertyName, typeof(GraphicsBuffer)),
            new(DurationsPropertyName, typeof(GraphicsBuffer)),
            new(TickIntervalsPropertyName, typeof(GraphicsBuffer))
        };

        public static IReadOnlyList<VfxDataShapeBuffer> BuffersFor(VfxDataShape shape) =>
            shape switch
            {
                VfxDataShape.Basic => BasicBuffers,
                VfxDataShape.Timed => TimedBuffers,
                _ => BasicBuffers
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
