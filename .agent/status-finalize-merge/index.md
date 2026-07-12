# Merge StatusProcessSystem into CombatApplyFinalizeSingleSystem

## Summary

Fold `StatusProcessSystem` into `CombatApplyFinalizeSingleSystem` as a second,
co-located pass and convert `StatusProcessJob` from `ScheduleParallel` (parallel
`IJobEntity`, `ParallelWriter` queues) to a **single-threaded Burst
`.Schedule()`** (plain `NativeQueue` enqueue). The merged system runs, in one
`OnUpdate`, exactly the two existing passes in the exact order they run today:

```
finalize pass (accrue stacks + apply damage + build presentation snapshot)   [only when hitCount > 0]
status pass   (decay + expire + detonate → enqueue spawn events)             [every frame with target stacks]
```

This is **behavior-preserving**. It is *not* the `broken` branch's change: we
keep **finalize-first ordering**, keep the `LastAccruedFrame`/`AccrualFrame`
decay guard, and keep same-frame threshold detonation. The only intended changes
are (a) one system instead of two, (b) status runs single-threaded, (c) the
`GetExistingSystemManaged` reach-in for `AccrualFrame` disappears (same-class
field now).

## Rationale

- The two systems are already hard-serial: both `RW TargetStackEntry` on the
  same target entities, so they can never overlap regardless of being one system
  or two. Co-locating removes the cross-system managed lookup and the ordering
  attributes coupling them, without changing the execution schedule.
- `AccrualFrame` is finalize-owned state that status only reads. Today that read
  crosses a system boundary via `World.GetExistingSystemManaged<...>()`
  ([StatusProcessSystem.cs:77](../../Assets/Scripts/System/Status/StatusProcessSystem.cs#L77)).
  In one class it is a private field — one source of truth, no reach-in.
- Status is memory-bound and tiny per entity; `ScheduleParallel`'s fan-out/merge
  overhead is negative ROI here (recorded prior finding). Single-thread also makes
  detonation enqueue order deterministic (query order) instead of thread-race
  order — a strict improvement, since ids already derive from
  `[EntityIndexInQuery]`, not enqueue order.

## Constraints & invariants the change must respect

- **Stack-buffer serialization.** Finalize (`FinalizeCombatSingleJob`, RW via
  `BufferLookup<TargetStackEntry>`) and status (`StatusProcessJob`, RW via query
  `DynamicBuffer<TargetStackEntry>`) both write the same buffer. They must stay
  serial, finalize first. *Source:*
  [CombatApplyFinalizeSingleSystem.cs:222](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L222),
  [StatusProcessSystem.cs:171-213](../../Assets/Scripts/System/Status/StatusProcessSystem.cs#L171-L213).
- **Finalize-first / same-frame detonation.** Finalize accrues `Count` and stamps
  `LastAccruedFrame = AccrualFrame`; status then detonates on `Count >= threshold`
  and skips decay for entries stamped this frame. Running finalize first keeps
  a threshold crossed *this frame* detonating *this frame* and keeps the
  refresh-then-don't-decay guard correct. Reversing this order is precisely what
  broke the `broken` branch. *Source:* also-working ordering
  ([CombatApplyFinalizeSingleSystem.cs:51](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L51)
  `UpdateBefore(StatusProcessSystem)`).
- **Status runs every frame — even with zero hits.** Decay/expire/detonate are
  time-driven. Today they run in a separate system that never sees finalize's
  `hitCount == 0` early return ([CombatApplyFinalizeSingleSystem.cs:128-132](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L128-L132)).
  In the merged system the finalize early-return **must not** skip the status
  pass. This is the load-bearing trap.
- **`AccrualFrame` increments once per frame, unconditionally**, before either
  pass reads it ([CombatApplyFinalizeSingleSystem.cs:113](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L113)).
  Keep it at the top of `OnUpdate`, above both passes.
- **Event-queue producer contract.** Status writes the three spawn-event lane
  queues that collision/timed producers also wrote this frame; those writes are
  tracked in each lane's `ProducerHandle`, not the system `Dependency`. Status
  must depend on those handles before writing, and expansion must wait on status
  before draining. *Source:*
  [StatusProcessSystem.cs:82-131](../../Assets/Scripts/System/Status/StatusProcessSystem.cs#L82-L131).
  Single-thread `.Schedule()` keeps this exact contract (combine lane handles into
  the deps, then publish `ProducerHandle = statusHandle`).
- **Determinism.** Ids come from `AoeIdBase`/`ProjectileDetonationSourceIdBase` +
  `[EntityIndexInQuery]`, not enqueue order. Single-thread does not change id
  assignment. *Source:* [StatusProcessSystem.cs:199](../../Assets/Scripts/System/Status/StatusProcessSystem.cs#L199).
- **Finalize keeps its blocking harvest.** Finalize schedules then
  `Dependency.Complete()` to read `results`/`statusSnapshots`/`evictionRef` on the
  main thread for the presentation bridge. Unchanged.

## Mechanisms reused vs. introduced

- **Reused:** finalize's blocking-harvest pattern; the lane `ProducerHandle`
  combine/publish dance (moved verbatim from status); the `AccrualFrame` +
  `LastAccruedFrame` guard; `ReserveIdBlock`; the presentation bridge handoff.
- **Introduced:** nothing new. No new type, component, queue, or system. Two
  fields (`nextAoeId`, `nextProjectileDetonationSourceId`) and one const
  (`MaxDetonationsPerTarget`) migrate onto the finalize system; the status job
  struct moves into the finalize file.
- **Removed:** `StatusProcessSystem` class + file; the
  `GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>()` reach-in; the
  `.ScheduleParallel` + `ParallelWriter` variant of the status job.

## Design validation

- *Serialization:* finalize scheduled and `.Complete()`d before status
  `.Schedule()` → status starts strictly after finalize's buffer writes. ✓
- *Finalize-first / same-frame detonation:* finalize pass literally precedes
  status pass in the same `OnUpdate`; guard and detonation timing identical to
  also-working. ✓
- *Zero-hit frames:* finalize job is gated on `hitCount > 0`, but the status pass
  is gated only on `targetStackQuery` non-empty and sits **after** the finalize
  block, not inside it — no `return` between them. ✓ (see 001 acceptance)
- *Producer contract:* lane-handle combine + `ProducerHandle = statusHandle`
  moved unchanged; expansion still waits on status via the published handle. ✓
- *Determinism:* id math unchanged; enqueue order becomes deterministic query
  order (no regression). ✓

## Minimal/additive vs. refactor comparison

- **Minimal/additive (keep two systems, only single-thread status):**
  - resulting data flow: unchanged two-system pipeline; status still reaches into
    finalize for `AccrualFrame` via managed lookup.
  - new concepts/types: none, but the cross-system reach-in and the duplicate
    ordering-attribute web survive.
  - copies/translations: none removed.
  - long-term cost: two systems that are provably always serial and share a
    private frame counter across a `GetExistingSystemManaged` boundary — the
    exact seam that made the reorder attempts fragile.
- **Refactor (merge into one system — chosen):**
  - resulting data flow: one system, two sequential passes, one owner of
    `AccrualFrame`.
  - existing concepts/types changed/removed: `StatusProcessSystem` deleted; the
    managed reach-in deleted; four expansion/finalize ordering attributes
    collapse.
  - copies/translations removed: the cross-system `AccrualFrame` read.
  - long-term benefit: the finalize↔status ordering is no longer expressible as a
    wrong attribute set; it is straight-line code, so the `broken`-class reorder
    hazard cannot recur by mis-attributing systems.
- **Decision:** **refactor** — it is what the user asked for and it removes the
  seam, with no new data path.

## Default decision rule

`AccrualFrame` is one domain concept (the frame stamp used by both accrue and
decay). It had two access paths (private field + managed cross-system read);
merge collapses to one source of truth. Conforms to the rule.

## Tasks

- [001-merge-system-and-single-thread.md](001-merge-system-and-single-thread.md)
  — move `StatusProcessJob` + status `OnUpdate` logic into
  `CombatApplyFinalizeSingleSystem`; convert status to single-threaded Burst
  `.Schedule()` with plain `NativeQueue`; restructure `OnUpdate` so the status
  pass runs on the `hitCount == 0` path; delete `StatusProcessSystem.cs`; drop the
  `GetExistingSystemManaged` reach-in.
- [002-reorder-attributes-and-comments.md](002-reorder-attributes-and-comments.md)
  — repoint the three expansion systems' `[UpdateAfter(StatusProcessSystem)]` to
  the merged system; remove finalize's `[UpdateBefore(StatusProcessSystem)]`;
  update the `CombatTargetProxy` flow comment. **Compile rider on 001.**
- [003-update-playmode-tests.md](003-update-playmode-tests.md) — remove the three
  test references to `StatusProcessSystem` (the merged system now covers both
  passes). **Compile rider on 001.**

001, 002, 003 must land together to compile. Split only for review granularity.

## Open questions / verification

- **Harness cannot build or run Unity.** Every task's real acceptance is a
  user-run PlayMode pass. The two decisive tests to run: `AoeSimulationTests`
  (detonation still fires same-frame on threshold cross; the L2/L3 stacking
  detonation chain at AoeSimulationTests.cs:1112) and
  `ProjectileCollisionSimulationTests`. Also eyeball total stack lifetime ≈
  authored `Lifetime` (decay timing intact) and confirm decay/expiry still occur
  on frames with **no** hits.
- One judgment call surfaced but **not** load-bearing: whether to keep the status
  pass async (`.Schedule` + publish `ProducerHandle`, letting expansion overlap
  it) or block it like finalize. Plan keeps it async — minimal delta from
  also-working, preserves the expansion overlap. Flagged in 001.
