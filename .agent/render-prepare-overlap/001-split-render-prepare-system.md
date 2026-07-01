# 001 — Split render prepare into an OrderFirst system

## Scope
Refactor. Single atomic change (rendering breaks if only half lands). Low
complexity — moves existing code, no new algorithm.

## Change

### New: `CombatRenderPrepareSystem`
File: `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`

- `[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]`
- `SystemBase`.
- Owns (moved from `CombatBatchedRenderSystem`):
  - `renderPrepareQuery` (`CombatKinematicsComponent`, `CombatRenderComponent`,
    `CombatRenderElement`, `CombatRenderActiveTag`).
  - `kinematicsHandle` (RO), `renderHandle` (RO), `elementHandle` (RW).
  - The `RenderPrepareJob` struct itself (or keep it public/shared; simplest is to
    move it into this file).
- `OnUpdate`:
  ```csharp
  kinematicsHandle.Update(this);
  renderHandle.Update(this);
  elementHandle.Update(this);

  Dependency = new RenderPrepareJob
  {
      Kinematics = kinematicsHandle,
      RenderComponents = renderHandle,
      RenderElements = elementHandle
  }.ScheduleParallel(renderPrepareQuery, Dependency);
  // NO CompleteDependency() here — leave the job in flight.
  ```

### Modified: `CombatBatchedRenderSystem`
File: `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

- Remove `renderPrepareQuery`, the three type-handle fields, the `RenderPrepareJob`
  schedule block, and the `RenderPrepareJob` struct (now owned by the prepare
  system).
- `OnUpdate` starts by ensuring the prepare job is done before any
  `CombatRenderElement` read:
  ```csharp
  CompleteDependency(); // completes the prepare job via this system's incoming Dependency
  ```
  `CombatBatchedRenderSystem` reads `CombatRenderElement`
  (`ToComponentDataArray`), so its incoming `Dependency` already includes the
  prepare job; `CompleteDependency()` is the same call previously made at line 88,
  now serving as the join point.
- Keep the registry loop, `SubmitBatchId`, `SubmitAll`, `submitBuffer`, and the
  `LastActiveProjectileCount`/`LastActiveAoeCount` counters unchanged.

## Ordering notes
- `CombatApplyBridge` is `UpdateBefore(CombatBatchedRenderSystem)` and
  `CombatVfxDispatchSystem` has no explicit order; with the prepare system at
  `OrderFirst` the effective presentation order is:
  `CombatRenderPrepareSystem` → {`CombatApplyBridge`, `CombatVfxDispatchSystem`}
  → `CombatBatchedRenderSystem` → `CombatStatsGatherSystem`.
- Do not add `[UpdateBefore]`/`[UpdateAfter]` coupling the prepare system to the
  managed systems yet; `OrderFirst` is enough and keeps it decoupled. (Fallback
  coupling is task 002 only if needed.)

## Acceptance criteria
- Project compiles; only one `RenderPrepareJob` definition exists.
- Projectiles and AOEs render in the correct positions, including entities spawned
  the same frame (verify a burst-spawn scene: no one-frame invisible pop or
  origin-flicker on fresh projectiles).
- No job-safety errors in the Editor with jobs debugger / safety checks enabled.
- Behavior is otherwise identical to before (same draw calls, same counts).

## Dependencies
None. Precedes task 002.
