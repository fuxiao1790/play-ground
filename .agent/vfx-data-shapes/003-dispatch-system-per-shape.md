# 003 — Dispatch system: per-shape bucketing & drain, one bridge

## Goal
Drain and dispatch every shape each presentation frame from the single
`CombatAoeVfxDispatchSystem`, preserving the one-`ProducerHandle`,
main-thread-drain contract. Bucketing keys on the **decoded per-shape local index**,
so each shape's dense index range keeps the counting-sort arrays small.

## Changes

### `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`
- In `OnUpdate`, after the single `ProducerHandle.Complete()` + reset and the
  `root != null` guard:
  - **Basic path** — bucket `PendingBasicSpawns` → `root.DrainAndDispatchBasic(...)`.
    Update `BucketBasicVfxSpawnsJob` (rename of `BucketAoeVfxSpawnsJob`) to bucket by
    `DecodeLocalIndex(VfxId)` with `BucketCount = root.RegisteredCountFor(Basic)`
    (was: raw id over total count).
  - **Timed path** — guarded by `PendingTimedSpawns.Count > 0`: run
    `BucketTimedVfxSpawnsJob` (counting-sort that also scatters
    `Duration`/`TickInterval`) with `BucketCount = root.RegisteredCountFor(Timed)`
    → `root.DrainAndDispatchTimed(...)`.
  - If `root == null`, clear **all** shape queues (mirror today's single-queue clear).
  - Sum every shape's dispatched count into `LastVfxEventCount` /
    `CombatStatsSingleton.VfxEventsCreated`.
- `BucketTimedVfxSpawnsJob` — `[BurstCompile] IJob` via `.Run()`, same counting-sort
  shape as the basic job, extended for the two extra arrays; same
  `math.max(0.01f, AreaSize)` clamp; key on `DecodeLocalIndex`.
- Both bucket jobs now share the "decode local index → counting sort" shape;
  extract a shared helper if it stays Burst-clean, else keep two jobs and note it.

## Acceptance criteria
- Compiles.
- Basic-only frames match the old behavior (same events, same graphs, same order
  within a bucket).
- Timed requests are counting-sorted by decoded local index and dispatched via the
  timed path; one `SendEvent` per timed graph per frame.
- `ProducerHandle` completed exactly once before any queue is read.
- Stats reflect all shapes.

## Dependencies
001, 002.

## Scope
Medium.
