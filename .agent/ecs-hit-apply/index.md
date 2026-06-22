# Multithreaded Hit Apply — ECS-owned HP, per-tick dispatch, parallel debuff

## Problem

Two post-collision stages were the frame bottleneck:

- **Single-instance damage dispatch** — damage crossed to the managed
  `ICombatTarget` **once per hit**. With dense frames this is O(hits) on the main
  thread (measured: `CombatApplyBridge.HitReplay` = 3.34ms for 3415 hits).
- **Single-threaded debuff application** — `StackAccrualSystem` drained a queue,
  **sorted**, grouped, and accrued stacks on the main thread.

Phase 1 (already implemented) fixed the debuff side and the grouping, but it kept
the **per-hit** managed dispatch, so the single-instance dispatch cost remained.
Phase 2 removes it.

## Solution

**ECS owns HP and status.** Damage is applied in the parallel finalize job to an
HP component on the target proxy, aggregated per target, and the GameObject
receives **one hit event per target per tick** (O(targets)) — the per-hit replay
is deleted. Status is already ECS-owned and accrued in parallel.

```
── Frame N: SimulationSystemGroup ──────────────────────────────────
Collision            → hitQueue.Enqueue(CombatHitEvent)        [PARALLEL, UNBOUNDED]

CombatApplyFinalize  flat = hitQueue.ToArray() → bucket map sized to count (no overflow)
 (before spawn)      [PARALLEL job over unique target keys]:
                       • roll crit (Unity.Mathematics.Random)
                       • accrue stacks → ECS accumulator
                       • SUM damage, count hits/crits
                       • TargetHealth.Current -= summed   (no clamp, may go negative)
                       • freeze ONE CombatTickResult per target

StatusProcess        [PARALLEL job over status entities] detonate (same frame) + reduce/decay
Spawn-expansion      materialize detonations (same frame)

── Frame N: PresentationSystemGroup ────────────────────────────────
CombatApplyBridge    [main thread] ONE ReceiveCombatTick(result, stacks) per target  ← O(targets)
Render
```

## Hard requirements (must hold)

These are non-negotiable and every task must preserve them:

1. **Hit events are bucketed; never sorted before applying damage.** Grouping is
   `NativeParallelMultiHashMap` + `GetUniqueKeyArray`. No `Sort` in the damage
   path. *(Status: met — `BucketHitsJob` + `GetUniqueKeyArray`,
   [CombatApplyBridge.cs:74-83](../../Assets/Scripts/System/Common/CombatApplyBridge.cs#L74).)*
2. **Multi-threaded end to end: production → bucketing → damage application.**
   Collision enqueues in parallel; `BucketHitsJob` is `IJobParallelFor`; damage
   application is the parallel `FinalizeCombatJob` (Phase 2). The single
   `HitQueue.ToArray` bulk copy **is allowed to be single-threaded** — it is a
   bulk memcpy that makes the queue indexable for the parallel bucket job, and is
   an accepted seam, not something to parallelize away (no `NativeStream` rework).
   Complete jobs **once**, not per stage. *(Status: production + bucketing met;
   application parallelized in 007.)*
3. **Results pushed to GameObjects at the end through a separate sync system, for
   both HP and status.** Apply/freeze (`CombatApplyFinalizeSystem`, sim) is
   distinct from the push (`CombatApplyBridge`, presentation), which delivers HP
   **and** status in one `ReceiveCombatTick` per target. *(Status: separate system
   exists; HP routed through it in 008.)*

## Key decisions (settled)

- **ECS owns HP and status.** The GameObject never writes them into ECS — it is a
  mirror. HP is seeded once at proxy creation from the target's max health; ECS
  owns it thereafter.
- **ECS does not clamp or decide death.** It subtracts into the negatives and
  pushes the value; the GameObject clamps, decides death, and drives feedback.
- **One hit event per target per tick** is acceptable. The per-hit `RolledHit[]`
  replay path is removed. `HitCount`/`CritCount`/`DamageTaken` are carried so the
  GameObject keeps accurate hurt/crit feedback ("preserve # of hits").
- **No condensing of distinct targets** — aggregation is per target, which is the
  natural grain once HP lives in ECS.
- **No DoT system exists** (skeleton at most). If one is added, its damage must
  enqueue a `CombatHitEvent` into the same pipeline rather than mutating the
  GameObject mirror — documented, not built.
- Debuff/status application stays the parallel per-target finalize job (Phase 1).

## Parallel-write safety

Each target / status entity is owned by exactly one job index (iteration over
`GetUniqueKeyArray` / per-entity), so HP, stack-buffer, and result writes via
`ComponentLookup`/`BufferLookup` with `[NativeDisableParallelForRestriction]`
never alias.

## Status

### Phase 1 — implemented

- `CombatHitEvent` (damage + stack payload); unbounded `NativeQueue` production;
  count-sized bucket `NativeParallelMultiHashMap` (no overflow under load).
- `CombatApplyFinalizeSystem` — parallel bucket + crit roll + stack accrual.
- `StatusProcessSystem` — parallel detonate + reduce/decay.
- `ReceiveStatus` / status snapshot push; `StackAccrualSystem`,
  `DamageDispatchBridge`, `DamageFinalizeSystem`, `DamageReplayEvent`,
  `StackApplyEvent` retired.

### Phase 2 — remaining (this update)

Converts the apply/dispatch half from per-hit transport to ECS-owned HP +
per-tick aggregate dispatch.

| # | File | Change | Summary | Depends |
|---|---|---|---|---|
| 006 | [006-target-health-component.md](006-target-health-component.md) | add | `TargetHealth` on the proxy archetype; `ICombatTarget.CombatMaxHealth`; seed once at `Create`. | — |
| 007 | [007-apply-and-aggregate-in-finalize.md](007-apply-and-aggregate-in-finalize.md) | adapt | Finalize job sums damage per target, subtracts from `TargetHealth` (no clamp), freezes `CombatTickResult`; remove the `RolledHit[]` per-hit path. | 006 |
| 008 | [008-per-tick-dispatch.md](008-per-tick-dispatch.md) | adapt | `ReceiveCombatTick(result, stacks)`; bridge pushes once per target; `MobRoot`/`PlayerRoot` mirror HP, decide death, drive feedback from counts; delete per-hit replay. | 007 |
| 009 | [009-docs-and-tests-update.md](009-docs-and-tests-update.md) | adapt | Correct `ecs-notes`/plan/memory (ECS owns HP); rework lethal-overkill test; add per-tick single-push test. | 008 |

> Tasks 001–005 are implemented and describe the Phase 1 per-hit model. The
> **push** parts of 001/004/005 are superseded by 007–009; their event/queue/
> bucket/finalize/status parts remain accurate.

## Constraints (document in code)

- ECS owns HP; the GameObject never writes HP/status into ECS.
- ECS never clamps HP or decides death — it may push negative HP.
- Production stays unbounded; bucket map sized to produced count (no dropped hits).
- The managed boundary is one `ReceiveCombatTick` per target per tick — never
  per hit. Assert O(targets) dispatch in tests.
- `HitCount`/`CritCount` preserved in the per-tick result.
- No `Sort` in the damage/debuff path; grouping is bucket + `GetUniqueKeyArray`.
- Production, bucketing, and damage application are all jobified; the apply system
  and the push (sync) system are distinct, and the push carries HP + status.
