---
name: collision-lookup-result-cap
description: Extract the spatial-hash cell-walk into a shared lookup type, decoupling it from AoeCollisionCore and ProjectileDiscreteCollisionSystem, then give the projectile path the same per-tick accepted-hit cap AOE already has.
---

# Decouple Spatial-Hash Lookup From Collision Jobs

## Summary

`AoeCollisionCore.RunCollision` and `ProjectileDiscreteCollisionSystem`'s
`ProjectileCollisionJob.Execute` each hand-write the identical mechanics for
walking a spatial-hash multi-hashmap: compute a cell range, nested `for`
loop over `(cx, cy)`, `TryGetFirstValue`/`TryGetNextValue` chaining, and a
same-faction skip on every candidate. [AoeCollisionCore.cs:71-149](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L71-L149)
and [ProjectileDiscreteCollisionSystem.cs:202-294](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L202-L294)
are two independent copies of this walk, each with its job-specific
narrowphase (dedup-or-gate check, `BoundsIntersect`, exact shape `Hit`,
emission) inlined directly into the same loop body.

The goal is to pull the cell-walk + faction-filter mechanics out into one
shared, reusable lookup type that both collision jobs drive, leaving each job
responsible only for what's genuinely different: its own per-candidate
rejection (AOE's per-pass `seenTargetKeys` dedup vs. projectile's persistent
`ProjectileContactGateElement` gate), the narrowphase test, and event
emission. Adding the per-tick accepted-hit cap to the projectile path (the
originally-requested change) becomes a natural, small addition once both
jobs sit on the same lookup — the cap is a collision-job-owned counter around
`MoveNext()`, not a lookup concern (see decision below).

`CombatSpatialHash` already generalizes the *cell math* (`FloorCell`,
`MinCell`/`MaxCell`, `CellKey`) across all three cell sizes (AOE, projectile,
tracking) — see [CombatSpatialHash.cs](../../Assets/Scripts/System/Api/Collision/Broadphase/CombatSpatialHash.cs).
What's missing is the equivalent generalization for *walking the
multi-hashmap itself*, which is what this plan adds.

## Constraints & Invariants

| Constraint | Source | Plan response |
|---|---|---|
| The per-tick cap counts **accepted hits**, not raw candidates pulled from the hash — AOE only decrements `remaining` after a candidate survives dedup and the exact shape test. | [AoeCollisionCore.cs:122,144](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L122) | **User decision**: the shared lookup stays an unbounded cell-walking enumerator. Each collision job keeps its own `remaining`/cap counter around calls to `MoveNext()`, exactly as `AoeCollisionCore` does today. The lookup type has no cap parameter and no knowledge of narrowphase acceptance. |
| Overflow keeps first-N in cell-scan order, not nearest-N. | [AoeCollisionCore.cs:64-66](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L64-L66) | The lookup preserves the inline loops' deterministic row-major **cell** order and yields each cell's values in the order supplied by `TryGetFirstValue`/`TryGetNextValue`. `NativeParallelMultiHashMap` is unordered, so insertion order or stable order among values sharing one key is not promised. No reordering is introduced by the lookup. |
| Both existing walks apply the same-faction skip as the very first per-candidate filter, before any narrowphase work. | [AoeCollisionCore.cs:86-87](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L86-L87), [ProjectileDiscreteCollisionSystem.cs:214-217](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L214-L217) | Fold faction exclusion into the lookup itself (constructor takes `targetFactions` + the excluded faction) — it's identical, non-domain-specific logic in both consumers, unlike dedup/gate which differ per job. |
| Cell-range derivation differs per consumer: AOE uses raw `collision.BoundsMin/BoundsMax`; projectile expands bounds by `MaxTargetRadius` first. | [AoeCollisionCore.cs:71-72](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L71-L72), [ProjectileDiscreteCollisionSystem.cs:196-200](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L196-L200) | The lookup takes an already-computed `cellMin`/`cellMax` (`int2`), not raw bounds. Cell-range derivation stays at each call site, using the existing `CombatSpatialHash` helpers. |
| Both jobs are `[BurstCompile]`; no managed allocation, no per-query native containers on the hot path. | [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs), [ProjectileDiscreteCollisionSystem.cs:110](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L110); precedent at [bvh-broadphase/index.md](../bvh-broadphase/index.md) constraints table | The lookup is a plain `struct` (value type) holding only the `NativeParallelMultiHashMap`, a `NativeArray<TargetFaction>` reference, cell-range bounds, and cursor state — stack-allocated, no `Allocator.Temp`/`Persistent` container of its own. `MoveNext(out int targetIndex)` mirrors the existing `ChunkEntityEnumerator` idiom already used in this codebase. |
| The cap is a safety bound, not a gameplay knob. | [CollisionConstants.cs:18-21](../../Assets/Scripts/System/Api/Collision/CollisionConstants.cs#L18-L21) | Unchanged from the original sub-plan: rename to a generic `MaxHitsPerTick`, single `const int`, no configurability. |
| Reaching the cap must not deactivate a projectile — only lifetime expiry, pierce exhaustion, or faction clear do. | [ProjectileDiscreteCollisionSystem.cs:160-185](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L160-L185) | Cap-reached exits the tick's scan via plain `return`, no `Deactivate` call. |
| Existing AOE and projectile tests assert exact current behavior. | [AoeSimulationTests.cs:410-459](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L410-L459), [ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs) | AOE migration must be behavior-preserving (existing tests pass unmodified in assertion logic). Projectile migration adds new behavior (the cap), covered by a new test, without breaking pierce/gate tests. |

## Mechanisms Reused vs. Introduced

**Reused:**
- `CombatSpatialHash`'s cell math (`MinCell`, `MaxCell`, `FloorCell`, `CellKey`) — untouched, still owns "position → cell coordinate."
- The manual-cursor-struct idiom already established by `ChunkEntityEnumerator` in this codebase — the new lookup follows the same `MoveNext(out T)` shape.
- `CollisionConstants` as the single home for spatial-hash safety bounds.
- The exact accepted-hit-cap pattern from `AoeCollisionCore` (job-owned `remaining` counter, decrement-after-accept, stop-scanning-at-zero) — applied to the projectile job around the new shared lookup instead of around an inline loop.

**Introduced:**
- One new type: a spatial-hash cell-walk enumerator in `Assets/Scripts/System/Api/Collision/Broadphase/`, constructed from a multi-hashmap, a faction-exclusion filter, and a cell range; yields candidate target indices one at a time.
- One renamed constant: `MaxAoeTargetsPerTick` → `MaxHitsPerTick`.
- One new `remaining` local + early-return in `ProjectileCollisionJob.Execute`.

No new managed types, no new native containers, no per-query allocation.

## Design Validation

- **Traversal preservation**: row-major cell order is deterministic. Within each cell, the lookup preserves the candidate order returned by the existing `TryGetFirstValue`/`TryGetNextValue` chain, so each migrated call site sees the same traversal its inline loop sees for the same built map. No insertion-order or cross-build bucket-order guarantee is claimed because `NativeParallelMultiHashMap` is unordered. Lookup tests verify cell precedence independently from within-cell membership; the AOE overflow test uses one relevant target per cell so its first-N expectation rests only on the supported cell-order guarantee.
- **Burst/allocation**: the lookup is a stack struct holding references into caller-owned containers (the multi-hashmap, the faction array) plus cursor state (`int2` cell position, current bucket iterator) — no allocation at construction or during `MoveNext`.
- **Behavior isolation**: AOE's accepted-hit cap semantics are byte-for-byte unchanged (same job-owned counter, same decrement-after-`EmitHit` point) — only the candidate-sourcing mechanics move into the shared type. Projectile gains the new cap as an independent stop condition alongside pierce, exactly as validated in the original sub-plan.
- **Faction filter placement**: folding it into the lookup removes one duplicated `if` per consumer and is safe because both jobs apply it identically and unconditionally before any job-specific logic — there's no case in either job where a same-faction candidate should reach dedup/gate/narrowphase.
- **What stays out of the lookup**: `BoundsIntersect` (AABB prefilter) and the exact shape `Hit` test stay at each call site — they're narrowphase geometry, not spatial-hash mechanics, and already live in `CombatCollisionMath` as shared static functions independent of this refactor.

- **Accepted-hit proof**: projectile coverage places more than `MaxHitsPerTick` broadphase candidates that fail narrowphase before later valid candidates. This makes decrement-on-candidate implementations fail and proves the budget moves only after an accepted hit.

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive** (the original sub-plan, now superseded): add a second, separately-written cell-walk loop inside `ProjectileCollisionJob.Execute` that mirrors `AoeCollisionCore`'s inline pattern, plus a renamed shared constant.
- resulting data flow: two independent hand-written copies of "walk cells, chain multi-hashmap iterators, skip same-faction" — now sharing a constant but not the mechanics.
- new concepts/types introduced: none; the duplication grows by one more consumer.
- copies/translations added: one more copy of the cell-walk loop.
- long-term cost: the exact coupling the user flagged — a future change to cell-walk mechanics (e.g. the BVH plan already in `.agent/bvh-broadphase/`, which is scoped to swap *only* the projectile discrete path's broadphase) would still have to be hand-applied to a bespoke loop instead of one shared type consumers plug into.

**Refactor** (this plan): extract the shared `MoveNext`-style lookup type; migrate both `AoeCollisionCore` and `ProjectileCollisionJob` to drive it; add the cap to the projectile job as a thin, job-owned wrapper around it.
- resulting data flow: one cell-walk-and-faction-filter implementation, two consumers, each retaining its own narrowphase/rejection/emission and its own accepted-hit budget.
- existing concepts/types changed: both `RunCollision` and `ProjectileCollisionJob.Execute` lose their inline nested loops in favor of a `while (lookup.MoveNext(out int targetIndex))` driver loop; behavior unchanged for AOE, cap added for projectile.
- copies/translations removed/avoided: removes the second hand-maintained copy of cell-walk mechanics before it's written, not after.
- long-term benefit: matches the user's explicit goal (decouple lookup from collision jobs); also directly sets up the already-planned BVH work in `.agent/bvh-broadphase/`, which targets swapping *only* the projectile discrete path's broadphase — having that path already depend on one lookup type (rather than an inline loop) narrows that future migration's surface instead of widening it.

**Decision:** refactor. This is the explicitly stated goal, and it also removes a structural warning (duplicate mechanism for the same concept) that the additive approach would have shipped.

## Task List

1. [001-generalize-hit-cap-constant.md](001-generalize-hit-cap-constant.md) — rename `MaxAoeTargetsPerTick` to `MaxHitsPerTick`. No behavior change.
2. [002-introduce-spatial-hash-lookup.md](002-introduce-spatial-hash-lookup.md) — add the new shared cell-walk enumerator type, with unit test coverage, no consumers wired yet.
3. [003-migrate-aoe-to-shared-lookup.md](003-migrate-aoe-to-shared-lookup.md) — switch `AoeCollisionCore.RunCollision` to drive the new lookup instead of its inline loop. Behavior-preserving; existing tests are the acceptance gate.
4. [004-migrate-projectile-to-shared-lookup.md](004-migrate-projectile-to-shared-lookup.md) — switch `ProjectileCollisionJob.Execute` to drive the same lookup, and add the `MaxHitsPerTick` accepted-hit cap as a job-owned counter. New test covers the cap.

## Open Questions

None remaining. Cap ownership was the one load-bearing ambiguity and is resolved above (lookup unbounded, job owns the cap). Scope is unchanged from the prior round: AOE behavior stays identical, continuous projectile collision and targeted/tracking acquisition are out of scope.
