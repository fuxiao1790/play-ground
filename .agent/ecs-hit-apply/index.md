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
during parallel production gives that grouping for free and deletes both sorts.

## Design

One unified hit event, bucketed by target proxy, then everything that can be
parallel is parallel. Only the managed push to GameObjects stays main-thread —
via the **same path used today** (sim freeze → presentation `ReceiveHits`).

```
── Frame N: SimulationSystemGroup ──────────────────────────────────
Collision            → map.Add(proxy, CombatHitEvent)          [PARALLEL writer]
                       (damage + stack payload, one event per hit, NO condensing)

HitApplyFinalize     [PARALLEL job over GetUniqueKeyArray()]   ← replaces drain + 2 sorts
 (before spawn)        • roll crit (Unity.Mathematics.Random)  ┐ rolled as applied
                       • accrue stacks → ECS accumulator        ┘
                       • freeze per-target rolled hits + status snapshot

StatusProcess        [PARALLEL job over status entities]
 (before spawn)        threshold → enqueue detonation spawn (same frame) + reduce/decay

Spawn-expansion      materialize detonations (same frame)

── Frame N: PresentationSystemGroup ────────────────────────────────
HitApplyBridge       [main thread] ReceiveHits (pre-rolled HP) + status push
Render

── Frame N+1 ───────────────────────────────────────────────────────
Collision            spawned detonation products collide → hit
```

## Key decisions (settled in discussion)

- **ECS is pure transport for HP.** It never reads HP, never clamps, never
  decides death — it pushes damage numbers to the GameObject even into the
  negatives. `MobRoot.CurrentHealth`/death stay the GameObject's authority
  ([MobRoot.cs:279-288](../../Assets/Scripts/Mob/MobRoot.cs#L279)).
- **Status accumulation stays in ECS** (the `TargetStackEntry` buffer on the
  proxy) so `StatusProcess` has entities to loop. **Status is also pushed to the
  GameObject** at the end, through the same managed path as the hit push.
- **No condensing** in this iteration — each hit stays a discrete event; we group
  by target but do not merge. Hit count is therefore naturally preserved.
- **Crit rolled inside ECS, as applied** — in the finalize job with deterministic
  `Unity.Mathematics.Random` (seed `math.hash(uint3(target.Index, frameCount,
  hitIndex))`), replacing the main-thread `UnityEngine.Random` roll. Crit becomes
  deterministic/replayable.
- **Push mirrors today's mechanism exactly** — a sim-side freeze into a stable
  array (today's `FinalizeDamageQueue`), consumed by a presentation-side managed
  push (today's `ReplayDamage`). Only the *freeze* changes from single-thread
  drain+sort to a parallel job.
- **Parallel writer, pre-sized** from a per-frame high-water mark with an overflow
  guard — keep it simple, no stream/bucketing second pass.
- **New systems slot at the tail of simulation**, right before the spawn-expansion
  systems (and thus before the presentation render).

## Parallel-write safety

Each target / status entity is owned by **exactly one job index** (iteration is
over `GetUniqueKeyArray` / per-entity), so writes through `BufferLookup` /
`ComponentLookup` with `[NativeDisableParallelForRestriction]` never alias. This
is the whole reason bucket-by-target beats sort-then-group: independence falls
out for free. (See `docs/simulation/ecs-notes.md` Part 3 + the "Target direction"
section, which this design implements.)

## What gets retired

`DamageFinalizeSystem`, `DamageDispatchBridge`, `StackAccrualSystem`,
`DamageReplayEvent`, and `StackApplyEvent` all collapse into the new pipeline.

## Tasks

| # | File | Change | Summary | Depends |
|---|---|---|---|---|
| 001 | [001-combat-hit-event-and-systems.md](001-combat-hit-event-and-systems.md) | add | `CombatHitEvent` + target-bucketed map; `HitApplyFinalizeSystem` (parallel crit-roll/freeze) + `HitApplyBridge` (managed HP push). Green-but-inert. | — |
| 002 | [002-switch-damage-producers.md](002-switch-damage-producers.md) | adapt | Collision systems write `CombatHitEvent` (damage half) to the map; remove `DamageReplayEvent`; retire `DamageFinalizeSystem` + `DamageDispatchBridge`. Activates HP path + ECS crit. | 001 |
| 003 | [003-parallel-status-pipeline.md](003-parallel-status-pipeline.md) | adapt | Carry stack payload in `CombatHitEvent`; accrue into ECS accumulator inside the finalize job; add `StatusProcessSystem` (parallel detonate + reduce/decay); retire `StackAccrualSystem` + `StackApplyEvent`. | 002 |
| 004 | [004-status-push-to-gameobject.md](004-status-push-to-gameobject.md) | add | New `ICombatTarget` status callback; bridge pushes per-target status snapshot alongside the HP push. | 003 |
| 005 | [005-docs-and-tests.md](005-docs-and-tests.md) | add | Update `ecs-notes.md`; playmode tests for parallel pipeline, hit-count preservation, crit determinism, detonation parity. | 004 |

## Sequencing & reviewability

- **001 is green-but-inert** — the new systems own an empty map; nothing produces
  into it yet, so behavior is unchanged.
- **002 activates the HP path** and removes the old damage systems in the same
  change (no double emission). Crit moves to ECS here.
- **003 is the largest task** — the status accrual/detonate swap is landed as one
  coherent change because splitting accrual from detonation leaves the
  accumulator with an inconsistent intermediate (two writers).
- **004** isolates the managed-interface addition (status callback).
- **005** last.

## Constraints (document in code)

- ECS must never branch on HP value (no clamp, no death decision). Verified by a
  test that pushes lethal+overkill damage and asserts ECS still emits.
- Single writer of the ECS status accumulator is the finalize job (accrue) +
  `StatusProcessSystem` (reduce) — never both at the same frame phase.
- The map is pre-sized; on overflow, log once and drop (never silently corrupt) —
  guard until sizing heuristics are validated by profiling.
- Hit count per target is preserved end-to-end (no condensing); assert in tests.
- Crit RNG seed must be frame-deterministic and entity-stable.
