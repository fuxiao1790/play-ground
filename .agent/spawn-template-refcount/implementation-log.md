# Implementation Log

## Status
Incomplete

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-registry-refcount-state.md | Complete | Added scope-owned count maps and delta queue; static diff check passed. |
| 002-combatroot-register-unregister-api.md | Complete | Managed owner claims, teardown-safe unregister, and pinned ad-hoc registrations added; static diff check passed. |
| 003-apply-instance-counting.md | Complete | Reworked to symmetric events: apply emits one acquire per spawn via `SpawnTemplateRefEmit`, gated on the slot going live. No slot-overwrite release. |
| 004-pool-cleanup-decrement.md | Complete | Reworked to despawn events at the six death sites. `CombatPoolCleanupSystem` reverted to HEAD — it needs no change under live-only counting. |
| 005-refcount-sweep-system.md | Complete | Added late single-threaded delta drain, dirty-gated paired-map sweep, and unregister dirty mark; static diff check passed. |
| 006-skilldriver-owner-lifecycle.md | Complete | SkillDriver now tracks every registration key and releases prior/rebound/destroyed claims in new-first order; static diff check passed. |
| 007-docs-and-tests.md | Incomplete | Updated docs and all affected custom-world fixtures; nine specified lifecycle tests remain to be added. |

## Completed Tasks
- 001-registry-refcount-state.md
- 002-combatroot-register-unregister-api.md
- 003-apply-instance-counting.md
- 004-pool-cleanup-decrement.md
- 005-refcount-sweep-system.md
- 006-skilldriver-owner-lifecycle.md

## Blockers
- 007: required new lifecycle test cases were not completed in this implementation pass.
- Nothing has been compiled or run. Unity build and test execution are deferred to the
  user by project rule, so every claim below is static inspection only.

## Design revision — symmetric spawn/despawn events
The first pass counted entities that carry a key whether live or pooled-dead, releasing
at slot overwrite and at pool destroy. Replaced with one acquire per spawn and one
release per despawn. Consequences:
- `CombatPoolCleanupSystem` reverted to HEAD.
- Slot-overwrite release removed from all three apply write points.
- Release added at six death sites, via the existing `ProjectileHitEmission.Deactivate`
  and `AoeCollisionCore.Deactivate` funnels plus the three lifetime jobs and
  `TargetedResolveSystem`.
- Five jobs gained `[WithPresent(typeof(TimedSpawnComponent))]`. This is the highest-risk
  detail: without it the query silently drops non-timed projectiles from collision.
- Runtime fix after first play: `LingeringAoeCollisionSystem` schedules with an explicit
  query, where `[WithPresent]` does not apply, so scheduling threw
  `InvalidOperationException: ... the query must (at the very minimum) contain all the
  components required for Execute() to run`. Its `EntityQueryBuilder` now also declares
  `.WithPresent<TimedSpawnComponent>()`. Audited the other custom-query jobs
  (`ImpactAoeCollisionSystem`, `TargetedResolveSystem`) — neither changed its `Execute`
  signature, so neither needs a builder entry.
- Acquire gated on `spawnState.Active` so inactive impact AOEs claim nothing.

## Validation Summary
- Unity tests deferred to user by project rule.
- 001: static source inspection and `git diff --check` passed.
- 002: static source inspection and `git diff --check` passed.
- 003: confirmed all five apply jobs receive only `Deltas` parallel writer; static diff check passed.
- 004: verified all key-bearing component reads are guarded by `ArchetypeChunk.Has`; static diff check passed.
- 005: verified direct singleton reads, dependency completion, after-cleanup ordering, and Temp key snapshots; static diff check passed.
- 006: verified every CombatRoot template registration call records exactly one matching key and all release paths clear lists; static diff check passed.
- 007: docs and direct-singleton test-world setup added; `git diff --check` passed. Unity execution deferred to user.
