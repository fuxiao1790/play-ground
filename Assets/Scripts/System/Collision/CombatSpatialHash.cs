using Unity.Mathematics;

namespace PlayGround.System.Common
{
    internal static class CombatSpatialHash
    {
        internal const float ProjectileCollisionCellSize = 2f;
        internal const float TrackingCellSize = 16f;
        internal const float AoeCellSize = 32f;

        internal static int2 FloorCell(float2 pos, float cellSize) =>
            new(
                (int)math.floor(pos.x / cellSize),
                (int)math.floor(pos.y / cellSize));

        internal static int2 MinCell(float2 min, float cellSize) => FloorCell(min, cellSize);

        internal static int2 MaxCell(float2 max, float cellSize) => FloorCell(max, cellSize);

        internal static long CellKey(int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
