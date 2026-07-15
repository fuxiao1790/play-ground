# 006 — Docs, lifecycle comments, verification

## Goal
Keep the design references and lifecycle comments in sync with the new path, and prove the
Area-Timed contract dispatches.

## Docs
- **`Docs/reference/simulation/vfx-system.md`** — add a short section: the runtime now supports a
  second request contract, `VfxAreaTimedRequest` (Positions + AreaSizes + Durations(ms) +
  TickIntervals(ms) + SpawnCount, event `OnSpawn`), selected per `(TypeId, Trigger)` via authored
  `VfxDataType` (flat `DataTypeByKey`, default `Area`). Note the Area path is unchanged and the two
  coexist. State the graph contract for an Area-Timed graph (four GraphicsBuffers).
- **`Docs/coding-standards.md:150-167` (Combat Event Separation)** — extend the VFX-lane bullet to
  name `VfxAreaTimedRequest` alongside `AoeVfxSpawnRequest` as visual-only payloads.
- If `Docs/flows/vfx-dispatch.md` / `Docs/contracts/vfx-requests.md` enumerate the payload, add the
  new struct there too.

## Lifecycle comments (`Docs/coding-standards.md:251-265`)
- Confirm `// ECS Lifecycle:` comments exist on `VfxAreaTimedRequest`, the singleton's
  `PendingAreaTimed`, and `DataTypeByKey` (added in 001/002).

## Verification
- Author one test/preview graph exposing the four buffers; drive it via the existing
  `VfxGraphSpawnTester` / `CombatVfxPreviewDriver` path, or a play-mode test, and confirm:
  - an Area-Timed-authored lingering AOE self-pulses on GPU for its Duration at TickInterval,
  - Area-authored effects are visually unchanged,
  - no unsafe, no per-frame GC alloc in the drain (Profiler),
  - teardown releases all four buffers (no leak warnings on exit).
- Existing VFX tests still pass (no migration => no test churn expected;
  `AoePulseVfxSystem`/`AoePulseVfxComponent` refs remain valid since that path is untouched).

## Acceptance criteria
- Docs and lifecycle comments describe both contracts and the `VfxDataType` selection.
- A manual/automated run shows the Area-Timed graph self-pulsing and the Area path unchanged.

## Dependencies
- 001-005.
