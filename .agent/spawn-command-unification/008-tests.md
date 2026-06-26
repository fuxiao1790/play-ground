# 008 — Tests

## Goal

Cover the unified path and the invariants, and lock in the originally-broken
stacking chain.

## Changes / cases

- **Cross-domain spawn via registry** (extend
  [ProjectileCollisionSimulationTests](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs),
  [AoeSimulationTests](../../Assets/Tests/PlayMode/AoeSimulationTests.cs)):
  projectile->AOE, projectile->projectile, AOE->projectile burst, AOE->AOE each
  materialize through expansion->apply from a registered template + invocation.
- **3-deep stacking chain**: lingering AOE -> OnImpactProjectile -> projectile ->
  StackTrigger -> stacking detonation accrues stacks and detonates at threshold.
- **Depth cap**: a 4-level authored chain warns and drops the overflow link.
- **Frozen registry**: assert no registry write occurs during a simulation tick
  (writes only via `CombatRoot.RegisterSpawnTemplate` in the managed phase).
- **Per-instance stamping**: contact-gate seed prevents immediate re-hit; impact
  projectiles aim back; tracking config survives onto burst projectiles.
- **Dedup**: identical follow-up behavior -> one key; changed count/spread -> new
  key; reselecting prior behavior -> prior key.
- **Determinism**: same seed/tick -> identical spawned ids.

## Acceptance criteria

- All new and existing spawn tests green.
- The originally-reported chain passes as a regression test.

## Dependencies

007.

## Scope

Medium–large.
