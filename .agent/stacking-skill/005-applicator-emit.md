# 005 — Applicator emits debuff key + per-stack contribution

## Change kind: adapt

## Structural role
The applicator output (the AOE/projectile the player casts) is what hits targets and
applies stacks. On hit it emits the single-level `StackApplyEvent`.

## Change
- The applicator's runtime snapshot carries: `DebuffKey`, `Threshold`, `Lifetime`, the
  per-stack `StackContribution` (detonation damage/X, count/X, area/X), and the
  `DetonationSnapshot` (type id + geometry). All baked at fire time in `SkillSpawnTranslator`.
- `AoeCollisionCore.EmitHit` (and the projectile collision equivalent, if the applicator can
  be a projectile): when the hit payload has stacking enabled, enqueue one `StackApplyEvent`
  to the `StackAccrualSystem` writer — replacing the old chain emit.
- Wire the stack writer into the applicator collision systems exactly as the existing
  damage/projectile writers are wired ([LingeringAoeCollisionSystem](Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs)).

## Structural notes
- `EmitHit` stays a pure intent emitter — no target-state mutation (single-writer invariant
  preserved).
- The contribution is fixed at fire time, so mid-build stat changes apply per stack.

## Acceptance criteria
- Each applicator hit enqueues one `StackApplyEvent` with the correct key + contribution.
- No `StackChainSnapshot` referenced in the emit path.

## Dependencies
004.

## Scope
Small–medium.
