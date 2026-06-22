# 005 — Docs and tests

**Change:** add · **Depends:** 004 · **Scope:** small-medium

## Goal

Document the new pipeline and lock its invariants with tests.

## Changes

1. **`docs/simulation/ecs-notes.md`** — update the "Known Design Issues: Collision
   Event Dispatch" / "Target direction" sections
   ([ecs-notes.md:302-353](../../docs/simulation/ecs-notes.md#L302)) to describe
   the implemented design: target-bucketed map, parallel finalize (ECS crit),
   parallel `StatusProcessSystem`, managed push unchanged. Note what is
   intentionally *not* done yet (condensing/aggregation).

2. **Playmode tests** (alongside
   [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs) /
   [ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs)):
   - **Hit-count preservation** — N hits on one target in a frame produce N
     `CombatHitData` entries (no condensing).
   - **Crit determinism** — same seed inputs → same crit outcomes across runs.
   - **ECS never decides death** — push lethal + overkill damage; assert ECS still
     emits the hit and the GameObject (not ECS) zeroes/handles HP, including
     negative pre-clamp values handed across the boundary.
   - **Detonation parity** — stack threshold detonation spawns the same
     AOE/projectile geometry/damage as the pre-refactor `StackAccrualSystem`
     (port/adapt existing stack tests).
   - **Status push** — `ReceiveStatus` fires with correct count/lifetime only on
     change.

3. **Memory** — update `project_stacking_skill.md` / add a pointer noting
   `StackAccrualSystem` was superseded by `HitApplyFinalizeSystem` (accrual) +
   `StatusProcessSystem` (tick/detonate), so the stacking-support plan's "ECS
   accrual reused" assumption now points at the new owners.

## Acceptance criteria

- All new + existing combat playmode tests pass.
- `ecs-notes.md` reflects the shipped design; no stale reference to
  `DamageDispatchBridge` / `StackAccrualSystem` as current.
- Memory index updated.

## Notes / risks

- If profiling motivated this work, capture a before/after frame-time note for
  the two stages in the index or memory so the win is recorded.
