# 006 — Validation polish & documentation

## Goal
Lock down exact-match validation and rewrite the design doc for the data-shape model
so future work checks against it.

## Changes

### Validation
- Confirm `ValidateGraphContract(asset, shape)` (task 002) enforces, against
  `VfxDataShapeTable[shape]`, a **hard fail** on any of: a required buffer missing,
  a required buffer with the wrong type, a missing `SpawnCount:int`/`OnSpawn`, or an
  **unexpected extra `GraphicsBuffer`-typed exposed property** beyond the shape's set.
  The message names the shape and the offending property; the id is `0`.
- Verify a null asset still returns id `0` and allocates nothing.
- Verify a valid registration returns an id that decodes to the registered shape.

### `Docs/reference/simulation/vfx-system.md`
Rewrite to the shipped model (the doc is also stale on the current bucketing — fix
that while here):
- **Summary / Data Flow:** ECS-owned `VfxDataShape` set; each graph bound to one
  shape; one combined `CombatAoeVfxDispatchSystem` → `CombatVfxRoot` →
  `CombatAoeVfxDispatcher`; one `ProducerHandle`; per-shape typed queues, per-shape
  counting-sort bucketing, per-shape dispatch; one id space.
- **Data shapes:** document `Basic` and `Timed`, their exact buffer sets, and that
  `VfxDataShapeTable` is the single source of truth used by validation, allocation,
  and dispatch. Name shapes by data (timing vs none), not by AOE use-case.
- **VFX Graph Contract:** per-shape contract; `Timed` graphs additionally expose
  `Durations`/`TickIntervals`; all shapes expose `Positions`/`AreaSizes`/
  `SpawnCount`/`OnSpawn`.
- **Timed authoring:** a `Timed`-shaped graph is emitted **once** and must self-drive
  its pulses over `Duration` at `TickInterval`, sampled in `Initialize Particles` →
  particle attributes (same transient-buffer rule as `AreaSizes`).
- **No category split:** impact vs lingering is a per-graph shape choice, not a
  system/lane split; emit routes by `DecodeShape(vfxId)`.
- **VfxId encoding:** the id encodes `(shape, per-shape localIndex)`, so `AoeVfxIds`
  stays 5 ints and jobs recover the shape without a side table.
- **Emitters / Authoring:** describe `VfxTimingData`, per-slot shape authoring on the
  prefab, and that the pulse slot/system are retained.
- **Performance Notes:** `Basic` is the lean hot path; extra buffers upload only for
  `Timed` graphs; still one `SendEvent`/graph/frame.

## Acceptance criteria
- A graph bound to a shape it doesn't match fails registration with a descriptive
  error and returns id `0`.
- `vfx-system.md` matches the shipped data-shape implementation and current bucketing;
  no stale single-payload or per-item-dequeue language remains.

## Dependencies
002, 003, 005.

## Scope
Small–medium (mostly docs).
