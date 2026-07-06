# 001 — Characterize gate coupling (prove render == Active)

## Goal
Prove, with PlayMode tests, that `CombatRenderActiveTag`'s enabled-state is always identical to
`Active` across the full lifecycle for both AOE and projectile — the invariant that makes the merge
in 002 safe. No behavior change; pure characterization. If the invariant does **not** hold, this task
refutes the merge and 002 is dropped.

## Tests to add (`Assets/Tests/PlayMode/`)
For AOE (in `AoeSimulationTests`) and projectile (in the projectile sim tests):

1. **At spawn**, across reuse and cold-create, for each variant (impact needs-collision, impact
   visual-only, lingering ±timed, projectile): assert `IsComponentEnabled<CombatRenderActiveTag>(e)
   == IsComponentEnabled<Active>(e)`.
2. **After a lifetime/collision despawn**: tick until the entity deactivates; assert both are disabled
   together (never one without the other).
3. **Mid-life**: assert both stay enabled together while the entity is live.

Prefer a small helper `AssertRenderTracksActive(entity)` reused across cases.

## Acceptance criteria
- Tests exist covering spawn / mid-life / despawn for AOE (both paths, all variants) and projectile.
- **User runs the suites: all green** → invariant holds → 002 is safe to consider.
- If any case shows render != Active, record it here; that case is the reason to KEEP the gate and 002
  is cancelled.

## Scope / complexity
Low. Assertions over existing spawn/tick/despawn helpers.

## Dependencies
Independent; should pass on the current base. It is the gate for 002 and the regression guard if 002
proceeds.
