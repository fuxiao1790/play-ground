# 007 — Apply damage to TargetHealth + aggregate per target

**Change:** adapt · **Depends:** 006 · **Scope:** medium

## Goal

Move HP subtraction into the parallel finalize job and aggregate per target.
This deletes the per-hit `RolledHit[]` path that drives the O(hits) managed
dispatch — the actual bottleneck.

## Changes

1. **`CombatTickResult`** (new blittable struct) — one per target:
   ```csharp
   struct CombatTickResult {
       Entity TargetProxy; float Health; float DamageTaken;
       int HitCount; int CritCount;
       int StatusStart; int StatusCount;   // reuse the existing status snapshot freeze
   }
   ```
   Replaces `RolledHit` + `TargetResultRange`
   ([CombatApplyBridge.cs:545-565](../../Assets/Scripts/System/Common/CombatApplyBridge.cs#L545)).

2. **`FinalizeCombatJob`** ([CombatApplyBridge.cs:204-293](../../Assets/Scripts/System/Common/CombatApplyBridge.cs#L204))
   — per target key, fold the bucket into one result instead of emitting per-hit:
   - keep the crit roll per hit (`Unity.Mathematics.Random`) and stack accrual.
   - accumulate `DamageTaken += rolledAmount`, `HitCount++`,
     `CritCount += isCrit ? 1 : 0` for `DirectDamageEnabled` hits.
   - subtract: `health = StackBuffers... ` → use a
     `ComponentLookup<TargetHealth>` (`[NativeDisableParallelForRestriction]`,
     one job index per target → no alias); `health.Current -= DamageTaken`
     (**no clamp**, may go negative); write back.
   - write one `CombatTickResult { Health = health.Current, DamageTaken, HitCount,
     CritCount, status range }`.

3. **Drop `CountHitsJob`** ([CombatApplyBridge.cs:180-202](../../Assets/Scripts/System/Common/CombatApplyBridge.cs#L180))
   — it only existed to size the per-hit `RolledHit[]`. Per-target results are
   one-per-key, so `keyCount` already sizes the output `NativeArray<CombatTickResult>`.

4. **Collapse the Complete() chain** — while restructuring, chain
   bucket → finalize as dependencies and `Complete()` once instead of the current
   three sequential completes ([CombatApplyBridge.cs:80,94,132](../../Assets/Scripts/System/Common/CombatApplyBridge.cs#L80)),
   so the main thread stalls once, not three times.

5. **Hand `NativeArray<CombatTickResult>` + status snapshots to the bridge**
   (`SetFinalizedCombat` signature changes; 008 consumes it).

## Acceptance criteria

- **Damage application is parallel** — `FinalizeCombatJob` (`IJobParallelFor` over
  target keys) reads the bucketed map and writes `TargetHealth`; no main-thread
  per-target loop applies damage.
- **No sort** — grouping stays bucket + `GetUniqueKeyArray`; verify no `Sort` call
  is introduced.
- `TargetHealth.Current` reflects summed damage each frame and is allowed to go
  negative (no clamp in ECS).
- Exactly one `CombatTickResult` per hit target per frame; `HitCount` equals the
  number of direct-damage hits for that target.
- No `RolledHit`/`CountHitsJob` remain; **one** `Complete()` in the finalize path
  (production → bucket → finalize chained as dependencies).
- Stack accrual + crit roll behavior unchanged from Phase 1.

## Notes / risks

- Crit still rolled per hit (seed unchanged); only the *output* aggregates. Total
  `DamageTaken` is order-independent, so bucket enumeration order does not matter.
- `TargetHealth` write and `TargetStackEntry` accrual happen in the same job pass
  per target — both guarded by one-index-per-target ownership.
