# 003 — Dispatcher Area-Timed resources + staging + drain

## Goal
Managed side owns a second resource kind with four typed buffers and dispatches it, reusing the
existing dispatcher's cap/validate/try-catch shape. Existing `AoeVfxTypeResources` path is untouched.

## Changes
- **`Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`**
  - Add `sealed class AoeVfxTimedTypeResources : IDisposable` mirroring `AoeVfxTypeResources` but
    with four buffers/staging lists:
    - `PositionBuffer` + `NativeList<float2> Staging`
    - `AreaSizeBuffer` + `NativeList<float> AreaSizeStaging`
    - `DurationBuffer` + `NativeList<float> DurationStaging`
    - `TickIntervalBuffer` + `NativeList<float> TickIntervalStaging`
    - `Dispose()` frees native lists first, then releases all four buffers, then destroys the GO
      (same order as the existing class, `CombatAoeVfxDispatcher.cs:31-51`).
  - Add property-name consts: `DurationsPropertyName = "Durations"`,
    `TickIntervalsPropertyName = "TickIntervals"`.
  - Second dictionary `Dictionary<int, AoeVfxTimedTypeResources> timedResources = new();` keyed with
    the existing `KeyFor`. (Keep `resources` for Area untouched.)
  - `RegisterAreaTimed(int typeId, AoeVfxTrigger trigger, VisualEffectAsset asset, int maxPerFrame)`
    — same try/catch/rollback shape as `Register` but allocates the four buffers and validates the
    graph exposes `Positions`, `AreaSizes`, `Durations`, `TickIntervals` (GraphicsBuffers) +
    `SpawnCount` (int). On any partial-alloc failure, dispose what was built.
  - `StageAreaTimed(int typeId, AoeVfxTrigger trigger, float2 pos, float area, float durMs,
    float tickMs)` — mirror `StageAoeSpawn`: `maxPerFrame` cap check, then push to all four lists.
  - Extend `Dispatch()` with a second loop over `timedResources` that `SetData`s all four buffers,
    `SetGraphicsBuffer`s them, `SetInt(SpawnCount)`, `SendEvent(OnSpawn)`, then clears all four
    staging lists. (Do not touch the existing Area loop.)
  - `Dispose()`: also iterate `timedResources`, remove from `LiveResources` if tracked, dispose,
    clear. Include the timed instances in `AliveParticleCount` (add them to `LiveResources`).
- **`Assets/Scripts/System/Vfx/CombatVfxRoot.cs`**
  - Add `public void RegisterAreaTimed(int typeId, AoeVfxTrigger trigger, VisualEffectAsset asset,
    int maxPerFrame = 2048)` -> `dispatcher?.RegisterAreaTimed(...)`.
  - `DrainAndDispatch` signature gains the second queue:
    `DrainAndDispatch(ref NativeQueue<AoeVfxSpawnRequest> area, ref NativeQueue<VfxAreaTimedRequest> timed)`.
    Drain the timed queue with `while (timed.TryDequeue(out var t)) { if (dispatcher.StageAreaTimed(
    t.TypeId, t.Trigger, t.Position, t.AreaSize, t.DurationMs, t.TickIntervalMs)) accepted++; }`
    before the single `dispatcher.Dispatch()`.
- **`Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`**
  - `OnUpdate` passes both queues to `root.DrainAndDispatch(ref area, ref timed)`.

## Notes / constraints
- Staging is fully typed -> `SetData<T>` only. No unsafe (`Docs/coding-standards.md:95`).
- Four separate typed buffers (not one struct buffer) per the VFX-Graph sample-node limitation.
- One `Dispatch()` call still, ordering between Area and Area-Timed loops irrelevant (unordered set).

## Acceptance criteria
- An Area-Timed graph registers, stages, and dispatches with all four buffers uploaded and
  `SpawnCount`/`OnSpawn` sent; a graph missing `Durations`/`TickIntervals` fails registration with a
  clear error and leaks nothing.
- Existing Area graphs behave identically (byte-for-byte same code path).
- Teardown releases all four buffers + destroys the timed GO on every path.

## Dependencies
- 002 (payload struct + queue).
