# Multithreaded Hit Apply — bucket-by-target, ECS crit, parallel status

## Problem

Profiling shows the two post-collision stages are the frame bottleneck, both
single-threaded:

- **Damage application** — `DamageFinalizeSystem` drains a `NativeQueue`
  single-threaded, then `DamageDispatchBridge` does `List.Sort` by target, rolls
  crit with `UnityEngine.Random`, and calls managed `ReceiveHits`
  ([DamageDispatchBridge.cs](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs)).
- **Stack accumulation** (worse) — `StackAccrualSystem` drains its own queue,
  **sorts** (`events.Sort(Comparer)`), groups by `(target, key)`, accrues into a
  per-target buffer, then ticks/fizzles/detonates — all on the main thread
  ([StackAccrualSystem.cs](../../Assets/Scripts/System/Status/StackAccrualSystem.cs)).

The sorts exist **only** to achieve per-target grouping. Bucketing by target
gives that grouping for free and deletes both sorts.

## Design

One unified hit event, produced into an **unbounded** queue, bucketed by target
proxy, then everything that can be parallel is parallel. HP **and** status cross
to the GameObject together in a **single combined push** per target — via the
same sim-freeze → presentation-push mechanism used today.

```
── Frame N: SimulationSystemGroup ──────────────────────────────────
Collision            → hitQueue.Enqueue(CombatHitEvent)        [PARALLEL writer, UNBOUNDED]
                       (damage + stack payload, one event per hit, NO condensing)

CombatApplyFinalize  [after collision, before spawn]
 (1) flat = hitQueue.ToArray()                                 ← serial memcpy, no sort
 (2) map = MultiHashMap(flat.Length)  [sized to ACTUAL count]  ← cannot overflow
 (3) [PARALLEL] insert flat → map  (target → hit index)
 (4) keys = map.GetUniqueKeyArray()                            ← grouping, no sort
 (5) [PARALLEL job over keys] per target:
        • roll crit (Unity.Mathematics.Random)  ┐ as applied
        • accrue stacks → ECS accumulator        ┘
        • freeze ONE combined result: rolled hits[] + status snapshot[]

StatusProcess        [PARALLEL job over status entities]
 (before spawn)        threshold → enqueue detonation spawn (same frame) + reduce/decay

Spawn-expansion      materialize detonations (same frame)

── Frame N: PresentationSystemGroup ────────────────────────────────
CombatApplyBridge    [main thread] ONE call per target: ReceiveCombat(hits, status)
Render

── Frame N+1 ───────────────────────────────────────────────────────
Collision            spawned detonation products collide → hit
```

## Key decisions (settled in discussion)

- **ECS is pure transport for HP.** Never reads HP, never clamps, never decides
  death — pushes damage numbers to the GameObject even into the negatives.
  `MobRoot.CurrentHealth`/death stay the GameObject's authority
  ([MobRoot.cs:279-288](../../Assets/Scripts/Mob/MobRoot.cs#L279)).
- **HP and status are pushed together** in one managed call per target
  (`ReceiveCombat`), not split across two callbacks/passes. Both halves come from
  the same frozen per-target record produced by the finalize job.
- **Status accumulation stays in ECS** (`TargetStackEntry` buffer on the proxy) so
  `StatusProcess` has entities to loop; the current snapshot rides the combined
  push to the GameObject.
- **No condensing** — each hit stays a discrete event; grouped by target, not
  merged. Hit count is naturally preserved.
- **Crit rolled inside ECS, as applied** — in the finalize job with deterministic
  `Unity.Mathematics.Random` (seed `math.hash(uint3(target.Index, frameCount,
  hitIndex))`), replacing main-thread `UnityEngine.Random`. Deterministic/replayable.
- **Production is unbounded; the bucket map is sized after the fact.** Collision
  writes an unbounded `NativeQueue<CombatHitEvent>.ParallelWriter` (same primitive
  as today's `DamageQueue` — no cap). The bucket `NativeParallelMultiHashMap` is
  allocated with capacity == the actual produced count, so high load can never
  exceed it. **No fixed cap, no dropped hits.**
- **Push mirrors today's mechanism** — sim-side freeze into a stable array
  (today's `FinalizeDamageQueue`), consumed by a presentation-side managed push
  (today's `ReplayDamage`). Only the *freeze* changes from single-thread
  drain+sort to a parallel bucket+finalize.
- **New systems slot at the tail of simulation**, right before the spawn-expansion
  systems (and thus before the presentation render).

## Parallel-write safety

Each target / status entity is owned by **exactly one job index** (iteration over
`GetUniqueKeyArray` / per-entity), so writes through `BufferLookup` /
`ComponentLookup` with `[NativeDisableParallelForRestriction]` never alias. This
is why bucket-by-target beats sort-then-group: independence falls out for free.
(See `docs/simulation/ecs-notes.md` Part 3 + "Target direction".)

## What gets retired

`DamageFinalizeSystem`, `DamageDispatchBridge`, `StackAccrualSystem`,
`DamageReplayEvent`, and `StackApplyEvent` all collapse into the new pipeline.

## Tasks

| # | File | Change | Summary | Depends |
|---|---|---|---|---|
| 001 | [001-combat-hit-event-and-systems.md](001-combat-hit-event-and-systems.md) | add | `CombatHitEvent` + unbounded queue + count-sized bucket map; `CombatApplyFinalizeSystem` (parallel crit-roll/freeze) + `CombatApplyBridge` (one combined `ReceiveCombat` push). Green-but-inert. | — |
| 002 | [002-switch-damage-producers.md](002-switch-damage-producers.md) | adapt | Collision systems enqueue `CombatHitEvent` (damage half); remove `DamageReplayEvent`; retire `DamageFinalizeSystem` + `DamageDispatchBridge`. Activates HP path + ECS crit. | 001 |
| 003 | [003-parallel-status-pipeline.md](003-parallel-status-pipeline.md) | adapt | Carry stack payload in `CombatHitEvent`; accrue into ECS accumulator in the finalize job; freeze the status half of the combined result; add `StatusProcessSystem` (parallel detonate + reduce/decay); retire `StackAccrualSystem` + `StackApplyEvent`. | 002 |
| 004 | [004-status-push-to-gameobject.md](004-status-push-to-gameobject.md) | add | GameObject consumes the status half of `ReceiveCombat` (`MobRoot`/player reflect stacks). Managed side only. | 003 |
| 005 | [005-docs-and-tests.md](005-docs-and-tests.md) | add | Update `ecs-notes.md`; tests for hit-count preservation, crit determinism, ECS-never-decides-death, detonation parity, **high-load (no dropped hits)**. | 004 |

## Sequencing & reviewability

- **001 is green-but-inert** — the new systems own an empty queue; nothing
  produces into it, behavior unchanged. The combined `ReceiveCombat` callback is
  defined here (status half empty until 003).
- **002 activates the HP path** and removes the old damage systems in the same
  change (no double emission). Crit moves to ECS here.
- **003 is the largest task** — the status accrual/detonate swap is landed as one
  coherent change (splitting accrual from detonation leaves the accumulator with
  two writers in an inconsistent intermediate). It fills the status half of the
  already-combined push.
- **004** is the managed-side consumption of the status half.
- **005** last.

## Constraints (document in code)

- ECS must never branch on HP value (no clamp, no death decision). Verified by a
  test that pushes lethal+overkill and asserts ECS still emits.
- **No fixed cap on hits.** Production is an unbounded queue; the bucket map is
  sized to the produced count each frame. A high-load test must assert zero
  dropped hits.
- Single writer of the ECS status accumulator is the finalize job (accrue) +
  `StatusProcessSystem` (reduce) — never both at the same frame phase.
- HP and status reach the GameObject in one `ReceiveCombat` call per target.
- Hit count per target is preserved end-to-end (no condensing); assert in tests.
- Crit RNG seed must be frame-deterministic and entity-stable.
