---
name: introduce-spatial-hash-lookup
description: Add a new shared cell-walk enumerator over the spatial-hash multi-hashmap, decoupled from any collision job. No consumers wired yet.
---

# 002 - Introduce the Shared Spatial-Hash Lookup

## Depends On

Nothing. This task only adds a new, unused-by-production-code type plus its
test. Tasks 003/004 wire it in.

## New Type

`Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashLookup.cs`,
namespace `PlayGround.System.Combat.Collision.Broadphase` (same as
`CombatSpatialHash`).

```csharp
internal struct TargetSpatialHashLookup
{
    internal TargetSpatialHashLookup(
        NativeParallelMultiHashMap<long, int> cells,
        NativeArray<TargetFaction> targetFactions,
        CombatFaction excludeFaction,
        int2 cellMin,
        int2 cellMax);

    internal bool MoveNext(out int targetIndex);
}
```

### Semantics

- Constructor takes an already-computed cell range (`cellMin`/`cellMax`) —
  callers derive this themselves via the existing `CombatSpatialHash` helpers
  (`MinCell`/`MaxCell`, with or without a radius expansion first). The lookup
  does not know about bounds, radii, or which cell-size constant applies.
- `MoveNext(out int targetIndex)` walks cells in the same order the two
  existing inline loops do today: `cy` from `cellMin.y` to `cellMax.y`, `cx`
  from `cellMin.x` to `cellMax.x` within each row, then bucket order within
  each cell via `TryGetFirstValue`/`TryGetNextValue`. Row-major cell order is
  deterministic. `NativeParallelMultiHashMap` is unordered, so order among
  values sharing one cell key is whatever that iterator chain supplies; the
  lookup preserves it but does not promise insertion order or stability across
  separately built maps.
- Same-faction candidates (`targetFactions[i].Value == excludeFaction`) are
  skipped internally — `MoveNext` never yields one. This is the one piece of
  filtering folded into the lookup (see index.md's Design Validation for why).
- No cap, no dedup, no gate check, no `BoundsIntersect`, no shape test — the
  lookup only knows "which target indices are geometrically-broadphase
  reachable and not on the excluded faction." Everything else stays at the
  call site.
- `MoveNext` returns `false` once every cell in range is exhausted. There is
  no external way to stop it early except the caller simply not calling it
  again (that's how the job-owned cap works in tasks 003/004 — the job's own
  loop condition stops calling `MoveNext`, the lookup itself never sees the
  cap).
- Value type, no `IDisposable` — it holds a reference to the caller-owned
  `NativeParallelMultiHashMap` and `NativeArray`, plus small cursor state
  (`int2` current cell, `int` current bucket value, the multi-hashmap
  iterator). No allocation in the constructor or in `MoveNext`.

## Acceptance Criteria

- New file compiles under Burst (used from within `[BurstCompile]` jobs in
  tasks 003/004 — verify by building, not just compiling in the editor).
- New EditMode test file `TargetSpatialHashLookupEditModeTests.cs` under
  `Assets/Tests/EditMode/`, namespace `PlayGround.Tests.EditMode` (matching
  [CombatSweepMathEditModeTests.cs](../../Assets/Tests/EditMode/CombatSweepMathEditModeTests.cs)'s
  convention), covering:
  - Empty range / empty hashmap → first `MoveNext` returns `false`.
  - Single cell, single entry, non-excluded faction → yields exactly that
    index once, then `false`.
  - Single cell, single entry, excluded faction → `MoveNext` returns `false`
    immediately (no candidates).
  - Multiple entries in one cell (same key, multiple hashmap values) → all
    are yielded before moving to the next cell.
  - Entries spread across multiple cells within the range → all yielded, in
    cell-scan order (assert the exact sequence, not just the set, to lock in
    the ordering guarantee the overflow test in task 003 depends on).
  - An entry in a cell outside `[cellMin, cellMax]` is never yielded.
  - Mixed excluded/non-excluded factions within the same cell → only
    non-excluded indices are yielded, in original bucket order.
- No behavior change anywhere else — this task touches no existing file.
