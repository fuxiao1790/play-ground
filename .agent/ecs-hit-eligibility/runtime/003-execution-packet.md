# Task Execution Packet

## Task
003-hit-count-and-tick-delta.md

## Goal
Make `CombatTickResult.HitCount` mean "accepted `CombatHitEvent` count for this
target in one finalizer update" (including non-damaging/status-only hits), and
add `TickDeltaSeconds` so the existing aggregate bridge callback tells a
managed target how much simulation time this tick's result covers.

## Files Allowed To Modify
- `Assets/Scripts/System/Application/CombatApplyResults.cs`
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading (do not modify)
- `Assets/Scripts/System/Presentation/CombatApplyBridge.cs` — confirms the
  bridge forwards `CombatTickResult` unchanged via
  `target.ReceiveCombatTick(in result, statusScratch)` and only skips a result
  when `result.HitCount <= 0 && result.StatusCount <= 0`. Do not modify this
  file — task 003 explicitly keeps the signature and skip condition as-is,
  just notes that the skip condition's behavior shifts because `HitCount` now
  increments for every accepted hit (see "Behavior To Change" below).
- `Assets/Scripts/System/Targets/ICombatTarget.cs` — confirms
  `ReceiveCombatTick(in CombatTickResult, IReadOnlyList<StatusStackSnapshot>)`
  signature and its default implementation (reads `result.DamageTaken` and
  `result.CritCount`, calls `ReceiveHit`/`ReceiveStatus`). Do not modify.

## Current Shapes (verified by orchestrator before packet creation)

`CombatTickResult` today (`CombatApplyResults.cs`):
```csharp
public struct CombatTickResult
{
    public Entity TargetProxy;
    public float Health;
    public float DamageTaken;
    public int HitCount;
    public int CritCount;
    public int StatusStart;
    public int StatusCount;
}
```

`CombatApplyFinalizeSingleSystem.FinalizeCombatSingleJob.Execute()` today:
- Dequeues every `CombatHitEvent` from `HitQueue` (each event was only
  enqueued by a producer if `payload.DirectDamageEnabled || payload.StackEffect.Enabled`
  — see `ProjectileHitEmission.HasHitEvent`/`AoeCollisionCore.HasHitEvent` — so
  every dequeued event here is already an "accepted" hit for this target, this
  task does not need to add any further eligibility check).
- For each dequeued hit, gets/creates a `TargetAccum` for `hit.Target`
  (`accums`/`map`), accrues status stack (`AccrueStack`, only if
  `payload.StackEffect.Enabled && acc.HasStackBuffer == 1`), and **only
  inside** `if (payload.DirectDamageEnabled)` does:
  ```csharp
  acc.DamageTaken += rolledAmount;
  acc.HitCount++;
  if (isCrit) { acc.CritCount++; }
  ```
  then unconditionally `acc.HitIndex++; accums[idx] = acc;`.
- After the dequeue loop, iterates `accums` and builds one `CombatTickResult`
  per target (`TargetProxy`, `DamageTaken`, `HitCount`, `CritCount`, plus
  `StatusStart`/`StatusCount` if `acc.StackChanged == 1`, plus `Health` if the
  target has a `Health` component — this health-snapshot logic is already
  unconditional on `DirectDamageEnabled`/damage, so it needs **no change**).
- The job struct (`FinalizeCombatSingleJob`) has no time/delta field today;
  `OnUpdate` schedules it via `.Schedule(Dependency)` without passing
  `SystemAPI.Time.DeltaTime`.

## Behavior To Preserve
- `DamageTaken` and `CritCount` remain direct-damage-only aggregates —
  computed only inside `if (payload.DirectDamageEnabled)`, exactly as today.
  Do not touch the crit-roll/damage-scale math.
- Status accrual (`AccrueStack`, `StatusStart`/`StatusCount` snapshot packing),
  per-target grouping (`map`/`accums`), queue draining, producer handle
  management, and result-list `Clear()`/`EnsureCapacity` behavior are
  unchanged.
- `Health` snapshot logic (`if (HealthLookup.HasComponent(acc.Target)) { ... }`)
  stays exactly as-is — it already runs unconditionally and already gives a
  coherent final-health snapshot even for a hit with no direct damage
  (`health.Current -= acc.DamageTaken` where `acc.DamageTaken` may be 0).
- `CombatApplyBridge`'s callback shape and its
  `result.HitCount <= 0 && result.StatusCount <= 0` skip condition are NOT to
  be changed by this task. The task's own acceptance criteria explicitly
  anticipate that, because every accepted hit now increments `HitCount`, this
  existing skip condition will now dispatch a callback for non-damaging/
  non-status accepted contacts too (previously such a hit could produce
  `HitCount == 0 && StatusCount == 0` and be skipped; now `HitCount >= 1` so it
  is no longer skipped). This is the intended, documented behavior change —
  do not "fix" it by editing the bridge.
- `ICombatTarget.ReceiveCombatTick`'s default body and signature are
  unchanged; actors that override it can now read `HitCount` and
  `TickDeltaSeconds` directly.
- One `CombatTickResult`/one bridge callback per target per finalizer update
  (never per-hit) — unchanged.

## Behavior To Change
1. In `CombatApplyResults.cs`, add to `CombatTickResult`:
   ```csharp
   public float TickDeltaSeconds;
   ```
   Add/refresh doc comments on the struct so:
   - `HitCount` is documented as: accepted `CombatHitEvent` count for this
     target during this finalizer update, including non-damaging/status-only
     hits.
   - `CritCount` and `DamageTaken` are documented as direct-damage-only
     aggregates (only accrued when `CombatHitPayload.DirectDamageEnabled`).
   - `TickDeltaSeconds` is documented as the source ECS finalizer's
     `SystemAPI.Time.DeltaTime` for the update that produced this result.

2. In `CombatApplyFinalizeSingleSystem.cs`:
   - Add a `public float DeltaTime;` field to `FinalizeCombatSingleJob` (name
     it to avoid clashing with the existing `Now`/`FrameCount` fields — e.g.
     `DeltaTime`), and pass `SystemAPI.Time.DeltaTime` into it at the job
     construction site in `OnUpdate` (alongside the existing `Now =
     SystemAPI.Time.ElapsedTime, FrameCount = (uint)UnityEngine.Time.frameCount`
     initializers).
   - Move `acc.HitCount++;` OUT of the `if (payload.DirectDamageEnabled)`
     block so it runs once for every dequeued hit reaching that point in the
     loop (i.e., for every target-accum update), while `acc.DamageTaken +=`,
     `if (isCrit) { acc.CritCount++; }`, and the crit-roll/random/seed logic
     stay inside the `DirectDamageEnabled` block exactly as today.
   - When constructing each `CombatTickResult` in the post-loop `for` over
     `accums`, set `TickDeltaSeconds = DeltaTime` (the job's new field) on
     every result — every result built in one `Execute()` call shares the same
     finalizer-update delta.

## Relevant Global Context
- Tick duration means ECS simulation update duration — the finalizer snapshots
  `SystemAPI.Time.DeltaTime` into each result; the bridge must never recompute
  timing from `UnityEngine.Time`. This task is the only place that stamps
  `TickDeltaSeconds`.
- Hit delivery stays aggregated per changed target; this task does not scale
  managed work with raw hit count — it only changes what `HitCount` counts and
  adds one float field, no new lane/event/container.
- `CombatFaction`/eligibility (tasks 001–002) are independent of this task at
  the code level — `FinalizeCombatSingleJob` only ever sees hits that already
  passed collision/acquisition eligibility upstream; nothing here needs to
  reference `TargetFaction` or `CanHit`.

## Dependencies Confirmed
- None required from tasks 001/002 at the code level (per index.md: "task 003
  is independent of tasks 001-002 at code level; tests combine both
  behaviors" — task 004 will exercise both together, not this task).
  Verified by reading `CombatApplyFinalizeSingleSystem.cs`: it consumes
  `CombatHitEvent`/`CombatHitPayload` only, no `TargetFaction` reference.

## Step-By-Step Instructions
1. Add `TickDeltaSeconds` field and refreshed doc comments to
   `CombatTickResult` in `CombatApplyResults.cs`.
2. Add `DeltaTime` field to `FinalizeCombatSingleJob` in
   `CombatApplyFinalizeSingleSystem.cs`; wire `SystemAPI.Time.DeltaTime` into
   it at the job-construction call site in `OnUpdate`.
3. Move `acc.HitCount++;` out of the `DirectDamageEnabled` branch so it runs
   unconditionally per dequeued hit (keep `acc.DamageTaken +=` and the
   crit-count increment inside that branch).
4. In the post-loop result-building `for` loop, set
   `result.TickDeltaSeconds = DeltaTime` (or include it in the `CombatTickResult`
   object initializer alongside `TargetProxy`/`DamageTaken`/`HitCount`/
   `CritCount`) for every result.
5. Re-read the whole `Execute()` method once more to confirm no other place
   needs `HitCount` or a delta value, and that `DamageTaken`/`CritCount`
   remain strictly inside the `DirectDamageEnabled` branch.

## Acceptance Criteria
- N accepted events for one target in one finalizer update yield one result
  with `HitCount == N` (regardless of how many were direct-damage vs.
  status-only).
- Status-only/non-damaging accepted events contribute to `HitCount` but not to
  `DamageTaken` or `CritCount`, and do not reduce `Health` (they never did,
  since `acc.DamageTaken` stays 0 for them).
- Every result contains the finalizer update's exact `SystemAPI.Time.DeltaTime`
  for that update (same value across every result produced by one `Execute()`
  call).
- Bridge callback (unmodified) receives these same values without consulting
  `UnityEngine.Time` — true by construction since the bridge only forwards the
  struct.
- Ticks with zero dequeued hits continue producing zero results (unchanged:
  `if (hitCount == 0) { singleton.HitQueue.Clear(); return; }` in `OnUpdate`
  already short-circuits before the job runs).
- Result delivery still scales with changed target count, not hit count (one
  `CombatTickResult` per accum entry, unchanged).
- No new event/result lane or managed per-hit replay is introduced.

## Validation Required
- Static/code-level check only (agent does not run Unity or tests): re-read
  the edited `Execute()` method to confirm `acc.HitCount++` is now outside the
  `if (payload.DirectDamageEnabled)` block while `acc.DamageTaken +=`/crit
  logic remain inside it, and that `TickDeltaSeconds` is set on every
  constructed `CombatTickResult`.
- Grep `CombatTickResult` construction sites project-wide (there should be
  exactly one, in this job) to confirm no other place builds this struct and
  needs the new field wired in.
- Do not attempt to run PlayMode/EditMode tests or the Unity compiler.

## Hard Boundaries
- Do not modify `CombatApplyBridge.cs` or `ICombatTarget.cs`.
- Do not add a batch context, alternate callback, or second result lane.
- Do not touch `TargetFaction`, `CanHit`, or any collision/acquisition/
  tracking system (tasks 001–002, already complete).
- Do not add tests (task 004) or documentation (task 005).
- Do not change architecture beyond the two described edits (`TickDeltaSeconds`
  field + doc comments; `HitCount` increment relocation + `DeltaTime` wiring).
- Do not introduce new abstractions.
- Do not combine this task with other tasks.
- Do not reopen index-level decisions.
- Stop and report if `FinalizeCombatSingleJob`'s structure doesn't match what
  this packet describes closely enough to make the edit unambiguous.
