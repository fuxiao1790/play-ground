# 001 — Shared `CombatSpatialHash` helper

## Goal
Extract the copy-pasted cell math into one shared static so build (producer) and query
(consumers) provably agree, and so cell sizes live in one place.

## Current duplication
- `ProjectileCollisionSystem`: `FloorCell` (cell 1), `CellKey` (FNV), `SpatialHashCellSize = 1`.
- `ProjectileTrackingSystem`: `FloorCell` (cell 64), `CellKey` (FNV), `TrackingSpatialHashCellSize = 64`.
- `AoeCollisionCore`: `MinCell`/`MaxCell` (cell 64), `CellKey` (FNV), `SpatialHashCellSize = 64`.

All three `CellKey` implementations are the identical FNV-1a over `(x,y)`.

## Change
Add `Assets/Scripts/System/Common/CombatSpatialHash.cs`:

```csharp
internal static class CombatSpatialHash
{
    // cell sizes preserved exactly; named per consumer intent
    internal const float ProjectileCollisionCellSize = 1f;
    internal const float TrackingCellSize = 64f;
    internal const float AoeCellSize = 64f;

    internal static int2 FloorCell(float2 pos, float cellSize) =>
        new((int)math.floor(pos.x / cellSize), (int)math.floor(pos.y / cellSize));

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
```

Keep the `TargetKey`/`TargetIdKey` helpers where they are for now (they are entity-identity,
not cell math); only cell math is unified here. (Optional follow-up: `TargetKey` is also
triplicated and identical to `CombatTargetProxy.TargetKey`; out of scope for this task.)

## Acceptance criteria
- `CombatSpatialHash` compiles under Burst (static, no managed state).
- No behavior change yet: this task only introduces the helper. The three systems are
  migrated to it in 002/003/004/005 as they are touched, or in this task if preferred —
  either way the FNV constants and `floor(pos/size)` math must be identical to today.

## Scope
Small. One new file; optional in-place substitution in the three existing systems.
