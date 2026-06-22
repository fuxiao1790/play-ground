# 009 — Docs and tests for ECS-owned HP + per-tick dispatch

**Change:** adapt · **Depends:** 008 · **Scope:** small-medium

## Goal

Correct the docs/plan/memory that still describe the per-hit "pure transport"
model, and update tests for ECS-owned HP and per-tick dispatch.

## Changes

1. **`docs/simulation/ecs-notes.md`** ([ecs-notes.md:303-347](../../Docs/simulation/ecs-notes.md#L303))
   — the section says "ECS still never reads target HP … decides death" and names
   `HitApplyFinalizeSystem`/`HitApplyBridge`/`ReceiveHits`. Rewrite to the shipped
   model: ECS **owns** HP via `TargetHealth`, applies damage in the parallel
   finalize job, pushes one `ReceiveCombatTick` per target per tick; the
   GameObject mirrors HP and decides death; correct the class/method names
   (`CombatApplyFinalizeSystem`/`CombatApplyBridge`/`ReceiveCombatTick`).

2. **Tests** ([AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs),
   [ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs)):
   - **Rework `EcsPushesLethalOverkillAndTargetOwnsHealthClamp`** — assert ECS
     `TargetHealth.Current` goes negative on overkill, the per-tick result carries
     that value, and the GameObject mirror clamps/decides death.
   - **Per-tick single dispatch** — N hits on one target produce one
     `ReceiveCombatTick` with `HitCount == N` (replaces the old per-hit
     `CombatApplyPreservesEveryHitForOneTarget`, which asserted N `CombatHitData`).
   - **HP applied in ECS** — after a frame, `TargetHealth.Current ==
     seed - summedDamage`.
   - Keep the high-load (no dropped hits), crit-determinism, and detonation-parity
     tests; adjust any that read `RolledHit`/per-hit ranges to the new
     `CombatTickResult`.

3. **Plan + memory** — this index already reflects Phase 2; update
   `project_ecs_hit_apply.md` to state ECS owns HP and dispatch is per-tick
   aggregate.

## Acceptance criteria

- All combat playmode tests pass against the new model.
- `ecs-notes.md` no longer claims ECS avoids HP; names match shipped classes.
- No test references `RolledHit` or per-hit dispatch counts.

## Notes / risks

- The "preserve # of hits" guarantee now lives in `CombatTickResult.HitCount`, not
  in a per-hit list — assert it there.
