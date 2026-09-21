# 003 - Report Accepted Hit Count And Tick Delta

## Goal

Make existing aggregate bridge result sufficient for any managed target to know
how many accepted contacts occurred in source simulation update and duration of
that update.

## Changes

1. In `Assets/Scripts/System/Application/CombatApplyResults.cs`:
   - add `float TickDeltaSeconds` to `CombatTickResult`;
   - document `HitCount` as accepted `CombatHitEvent` count for target in one
     finalizer update;
   - document `CritCount` and `DamageTaken` as direct-damage-only aggregates.

2. In `CombatApplyFinalizeSingleSystem`:
   - pass `SystemAPI.Time.DeltaTime` into `FinalizeCombatSingleJob`;
   - increment accumulator `HitCount` once for every valid dequeued hit after
     source payload and target accumulator resolution, outside
     `DirectDamageEnabled` branch;
   - retain crit roll, `DamageTaken`, and `Health` mutation inside direct-damage
     branch;
   - stamp same tick delta on every `CombatTickResult` created by job;
   - leave status accrual, per-target grouping, queue draining, producer handle,
     and result clearing unchanged.

3. In `CombatApplyBridge` and `ICombatTarget` contract:
   - keep existing `ReceiveCombatTick(in CombatTickResult, stacks)` signature;
   - bridge forwards result unchanged, so actor overrides can read `HitCount` and
     `TickDeltaSeconds` directly;
   - keep one callback per result, never one callback per hit;
   - because every accepted hit now increments `HitCount`, current skip condition
     dispatches non-damaging/non-status contacts too;
   - do not add batch context, alternate callback, or result lane.

4. Confirm all target results carry final health when proxy has `Health`, even
   when accepted event has no direct damage, preserving a coherent snapshot.

## Acceptance Criteria

- N accepted events for one target in one finalizer update yield one result with
  `HitCount == N`.
- Status-only/non-damaging accepted events contribute to `HitCount` but not
  `DamageTaken` or `CritCount` and do not reduce `Health`.
- Every result contains finalizer update's exact `SystemAPI.Time.DeltaTime`.
- Bridge callback receives same result values without consulting
  `UnityEngine.Time`.
- Ticks with no result continue producing no managed callback.
- Result delivery still scales with changed target count.
- No new event/result lane or managed per-hit replay exists.

## Dependencies

- Independent of tasks 001-002 at code level; tests combine both behaviors.

## Estimated Scope / Complexity

Low to medium. Compact contract and aggregation semantic change with regression
impact for status-only hit expectations.

