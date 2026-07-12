# 001 — Defer the apply job in CombatApplyFinalizeSingleSystem

## Change
In `OnUpdate`, after scheduling `FinalizeCombatSingleJob`:
- **Remove** `Dependency.Complete()` and everything downstream of it that reads job output on
  the main thread: `evictionRef.Value`, the eviction-counter update, `resultsList.ToArray()`,
  `statusList.ToArray()`, the TempJob disposes, and `bridge.SetFinalizedCombat(...)`.
- Keep `Dependency = <applyHandle>` so later sim systems that touch `TargetHealth`/
  `TargetStackEntry` still chain correctly.
- Hand the **un-completed** pending to the consumer:
  `bridge.SetPendingCombat(applyHandle, resultsList, statusList, evictionRef)`.
- Fallback when `bridge == null` (worlds/tests without presentation): complete the handle and
  dispose the three containers inline, exactly as today — no behavior change there.

The apply job still `TryDequeue`s `HitQueue` itself, so no `HitQueue.Clear()` is needed on the
hit path (unchanged); the `hitCount == 0` early-return still clears and returns.

## Pending lifecycle (the subtle part)
The apply job reads/drains `HitQueue` and writes `TargetHealth`/`TargetStackEntry`; it is now
in flight when `OnUpdate` returns. Guarantee it is completed before the next Finalize touches
`HitQueue`:
- At the **start** of `OnUpdate` (repurpose the current `bridge?.DisposeFinalizedCombat()`
  step): call `bridge?.DiscardPending()` which **completes** any outstanding apply handle and
  disposes its containers **and** disposes last frame's materialized `finalizedResults`.
- Normal in-game path: presentation ran between sim frames, so pending was already consumed —
  discard finds nothing. Tests without a `presentationGroup.Update()` between ticks: discard
  completes+drops the stale pending so `HitQueue` is safe to read. This bounds pending to one
  sim tick (avoids TempJob 4-frame expiry).

## Allocators
Keep `resultsList`/`statusList`/`evictionRef` as `Allocator.TempJob` (unchanged). They live
from Finalize (sim) to the bridge (presentation) — same frame, explicitly completed before
read, disposed after. Only the *location* of complete/ToArray/dispose moves.

## Risk
Silent-failure class: use-after-dispose, double-dispose, or an un-completed handle at teardown.
`CombatApplyBridge.OnDestroy` must complete+dispose any pending (see 002).
