# 002 — Root, dispatcher & resources: shape-aware register/validate/dispatch

## Goal
Registered graphs carry their shape via the encoded id, allocate exactly that
shape's buffers, validate the asset against `VfxDataShapeTable` with a **hard fail**
on any mismatch, and dispatch each shape. Presentation stays single-source: one
dispatcher, one root, one id space (partitioned by shape in the id bits).

## Changes

### `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`
- `AoeVfxTypeResources`:
  - Add `public VfxDataShape Shape;`
  - Own exactly the buffers required by `Shape`
    (`Basic` → Positions, AreaSizes; `Timed` → + Durations, TickIntervals), count
    from `VfxDataShapeTable.BufferCountFor(Shape)`. `EnsureBufferCapacity` grows all
    in lockstep (same doubling + allocate-new-then-release-on-failure); `Dispose`
    releases all.
- `CombatAoeVfxDispatcher`:
  - `ValidateGraphContract(asset, VfxDataShape shape, out reason)` — **hard fail**
    (return false) if the asset does not expose *exactly* `VfxDataShapeTable[shape]`:
    every listed buffer present with correct type, `SpawnCount:int`, `OnSpawn`, **and
    no unexpected extra `GraphicsBuffer`-typed exposed property** beyond the shape's
    set. `reason` names the shape and the offending property.
  - `Dispatch(res, ...)` per shape. Factor the shared upload core (Positions,
    AreaSizes, SpawnCount, `SendEvent(OnSpawn)`) into a private helper; the `Timed`
    path also uploads `Durations`/`TickIntervals`. No duplication.

### `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- Hold resources **per shape**: e.g. `List<AoeVfxTypeResources> ownersByShape[shapeOrdinal]`
  (or a fixed small array of lists). `idsByAsset` still maps asset → encoded id
  (globally unique because shape bits differ).
- `Register(VisualEffectAsset asset, VfxDataShape shape)`:
  - `null` asset → `0`.
  - known asset: return its stored id; if the stored resource's `Shape` differs from
    `shape`, log a conflict and keep the original (mirror the contract-conflict log).
  - validate via `ValidateGraphContract(asset, shape, ...)`; on failure log + return `0`.
  - append to `ownersByShape[shape]`, set `res.Shape`, allocate shape buffers;
    `id = VfxDataShapeTable.EncodeId(shape, ownersByShape[shape].Count)` (1-based).
  - drop the old `requireAreaSizeContract` bool (superseded by shape).
- Expose `RegisteredCountFor(VfxDataShape shape)` for the dispatch system's bucketing.
- Per-shape drain entry points: `DrainAndDispatchBasic(...)` (iterates
  `ownersByShape[Basic]` by local index) and `DrainAndDispatchTimed(...)` (iterates
  `ownersByShape[Timed]`, passing the extra sorted arrays). Look up a resource by
  `DecodeLocalIndex(id) - 1` within its shape's list.

## Acceptance criteria
- Compiles.
- A `Basic` graph owns 2 buffers and dispatches identically to today; a `Timed`
  graph owns 4 and uploads all four + SpawnCount + OnSpawn.
- A graph missing a required buffer, with a wrong type, **or with an extra
  `GraphicsBuffer`** fails registration with a descriptive error and returns `0`.
- Returned ids decode back to the correct shape + local index.
- No duplicated upload code across shapes.

## Dependencies
001.

## Scope
Medium.
